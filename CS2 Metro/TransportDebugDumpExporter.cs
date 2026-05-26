using Game;
using Game.SceneFlow;
using Game.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace CS2_Metro
{
    public static class TransportDebugDumpExporter
    {
        private const int MaxSamplesPerCandidateType = 20;
        private const int MaxComponentReadsPerSample = 16;
        private const int MaxBufferElementSamples = 5;
        private const int MaxFieldReadsPerComponent = 24;

        private static readonly string[] CandidateKeywords =
        {
            "publictransport",
            "public transport",
            "transport",
            "transit",
            "line",
            "route",
            "stop",
            "station",
            "metro",
            "subway"
        };

        private static readonly string[] ReadPriorityKeywords =
        {
            "name",
            "color",
            "colour",
            "position",
            "pos",
            "transform",
            "coordinate",
            "location",
            "route",
            "stop",
            "station",
            "line",
            "transport",
            "transit",
            "metro",
            "subway",
            "type",
            "mode",
            "owner",
            "target",
            "source",
            "waypoint",
            "next",
            "previous"
        };

        public static string GetDefaultExportDirectory()
        {
            return TestMetroJsonExporter.GetDefaultExportDirectory();
        }

        public static string GetJsonPath()
        {
            return Path.Combine(GetDefaultExportDirectory(), "debug-dump.json");
        }

        public static string GetTextPath()
        {
            return Path.Combine(GetDefaultExportDirectory(), "debug-dump.txt");
        }

        public static bool ExportDebugDump(UpdateSystem updateSystem)
        {
            string jsonPath = GetJsonPath();
            string textPath = GetTextPath();
            Mod.log.Info($"Export Transport Debug Dump started. JSON: {jsonPath}. TXT: {textPath}");

            try
            {
                Directory.CreateDirectory(GetDefaultExportDirectory());

                TransportDebugDumpDocument document = CreateDump(updateSystem, jsonPath, textPath);
                WriteJson(jsonPath, document);
                File.WriteAllText(textPath, BuildTextReport(document), new UTF8Encoding(false));

                MetroTrackGeometryDebugExporter.Export(updateSystem);

                Mod.log.Info($"Export Transport Debug Dump succeeded. JSON: {jsonPath}. TXT: {textPath}");
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Export Transport Debug Dump failed. JSON: {jsonPath}. TXT: {textPath}. Error: {ex}");
                return false;
            }
        }

        private static TransportDebugDumpDocument CreateDump(UpdateSystem updateSystem, string jsonPath, string textPath)
        {
            TransportDebugDumpDocument document = new TransportDebugDumpDocument
            {
                dumpVersion = VersionInfo.ReleaseVersion,
                exportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                jsonPath = jsonPath,
                textPath = textPath,
                sampleLimitPerCandidateType = MaxSamplesPerCandidateType,
                environment = CreateEnvironment(updateSystem)
            };

            if (updateSystem == null)
            {
                document.warnings.Add("UpdateSystem is null. The mod may not be fully loaded yet.");
                return document;
            }

            World world = null;
            try
            {
                world = updateSystem.World;
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Failed to access UpdateSystem.World: {ex}");
            }

            if (world == null || !world.IsCreated)
            {
                document.warnings.Add("World is not available or is not created. No ECS entity data was scanned.");
                return document;
            }

            try
            {
                ScanEntities(updateSystem.EntityManager, document);
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Entity scan failed: {ex}");
                Mod.log.Error($"Transport debug dump entity scan failed: {ex}");
            }

            try
            {
                document.routeGeometryDiagnostics = RouteGeometryDiagnostics.BuildSubwayTransportLineReport(updateSystem.EntityManager, TryGetNameSystem(world));
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Route geometry diagnostics failed: {ex}");
                Mod.log.Error($"Transport debug dump route geometry diagnostics failed: {ex}");
            }

            return document;
        }

        private static TransportDebugEnvironment CreateEnvironment(UpdateSystem updateSystem)
        {
            TransportDebugEnvironment environment = new TransportDebugEnvironment
            {
                hasUpdateSystem = updateSystem != null,
                gameMode = SafeGameManagerValue(() => GameManager.instance.gameMode.ToString()),
                gameState = SafeGameManagerValue(() => GameManager.instance.state.ToString()),
                shouldUpdateWorld = SafeGameManagerValue(() => GameManager.instance.shouldUpdateWorld.ToString()),
                isGameLoading = SafeGameManagerValue(() => GameManager.instance.isGameLoading.ToString())
            };

            if (updateSystem == null)
            {
                return environment;
            }

            try
            {
                World world = updateSystem.World;
                environment.hasWorld = world != null;
                if (world != null)
                {
                    environment.worldIsCreated = world.IsCreated;
                    environment.worldName = world.Name;
                }
            }
            catch (Exception ex)
            {
                environment.worldName = $"Failed to read world: {ex.GetType().Name}: {ex.Message}";
            }

            return environment;
        }

        private static string SafeGameManagerValue(Func<string> read)
        {
            try
            {
                return GameManager.instance == null ? "GameManager.instance is null" : read();
            }
            catch (Exception ex)
            {
                return $"Failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private static NameSystem TryGetNameSystem(World world)
        {
            try
            {
                return world != null && world.IsCreated ? world.GetExistingSystemManaged<NameSystem>() : null;
            }
            catch
            {
                return null;
            }
        }

        private static void ScanEntities(EntityManager entityManager, TransportDebugDumpDocument document)
        {
            Dictionary<string, TransportCandidateGroup> groups = new Dictionary<string, TransportCandidateGroup>();
            HashSet<int> candidateEntityIndexes = new HashSet<int>();

            using (NativeArray<Entity> entities = entityManager.GetAllEntities(Allocator.Temp))
            {
                document.environment.totalEntityCount = entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];

                    try
                    {
                        if (!entityManager.Exists(entity))
                        {
                            continue;
                        }

                        List<ComponentType> componentTypes = GetComponentTypes(entityManager, entity);
                        List<ComponentType> matchingTypes = componentTypes
                            .Where(IsCandidateComponentType)
                            .ToList();

                        if (matchingTypes.Count == 0)
                        {
                            continue;
                        }

                        candidateEntityIndexes.Add(entity.Index);
                        List<string> componentTypeNames = componentTypes.Select(GetComponentTypeName).OrderBy(name => name).ToList();

                        foreach (ComponentType matchingType in matchingTypes)
                        {
                            TransportCandidateGroup group = GetOrCreateGroup(groups, matchingType);
                            group.totalEntityCount++;

                            if (group.samples.Count < MaxSamplesPerCandidateType)
                            {
                                group.samples.Add(CreateEntitySample(entityManager, entity, componentTypes, componentTypeNames));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        document.exceptions.Add($"Failed to inspect entity {entity.Index}:{entity.Version}: {ex}");
                    }
                }
            }

            document.environment.candidateEntityCount = candidateEntityIndexes.Count;
            document.environment.candidateComponentTypeCount = groups.Count;
            document.candidateGroups = groups.Values
                .OrderByDescending(group => group.totalEntityCount)
                .ThenBy(group => group.componentType)
                .ToList();
        }

        private static List<ComponentType> GetComponentTypes(EntityManager entityManager, Entity entity)
        {
            using (NativeArray<ComponentType> componentTypes = entityManager.GetComponentTypes(entity, Allocator.Temp))
            {
                List<ComponentType> results = new List<ComponentType>(componentTypes.Length);
                for (int i = 0; i < componentTypes.Length; i++)
                {
                    results.Add(componentTypes[i]);
                }

                return results;
            }
        }

        private static TransportCandidateGroup GetOrCreateGroup(Dictionary<string, TransportCandidateGroup> groups, ComponentType componentType)
        {
            Type managedType = SafeGetManagedType(componentType);
            string key = managedType != null ? managedType.FullName : componentType.ToString();

            if (groups.TryGetValue(key, out TransportCandidateGroup existing))
            {
                return existing;
            }

            TransportCandidateGroup group = new TransportCandidateGroup
            {
                componentType = key,
                componentNamespace = managedType != null ? managedType.Namespace : string.Empty,
                componentCategory = GetComponentCategory(componentType),
                matchedKeywords = GetMatchedKeywords(key).ToList()
            };
            groups.Add(key, group);
            return group;
        }

        private static TransportEntitySample CreateEntitySample(EntityManager entityManager, Entity entity, List<ComponentType> componentTypes, List<string> componentTypeNames)
        {
            TransportEntitySample sample = new TransportEntitySample
            {
                entity = $"{entity.Index}:{entity.Version}",
                entityIndex = entity.Index,
                entityVersion = entity.Version,
                entityName = SafeReadEntityName(entityManager, entity),
                componentTypes = componentTypeNames
            };

            List<ComponentType> readableComponents = componentTypes
                .Where(ShouldReadComponentDetails)
                .Take(MaxComponentReadsPerSample)
                .ToList();

            foreach (ComponentType componentType in readableComponents)
            {
                sample.componentData.Add(ReadComponent(entityManager, entity, componentType));
            }

            return sample;
        }

        private static string SafeReadEntityName(EntityManager entityManager, Entity entity)
        {
            try
            {
                return entityManager.GetName(entity);
            }
            catch (Exception ex)
            {
                return $"Failed to read entity name: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private static ComponentReadResult ReadComponent(EntityManager entityManager, Entity entity, ComponentType componentType)
        {
            Type managedType = SafeGetManagedType(componentType);
            string typeName = managedType != null ? managedType.FullName : componentType.ToString();
            ComponentReadResult result = new ComponentReadResult
            {
                componentType = typeName,
                category = GetComponentCategory(componentType)
            };

            try
            {
                if (componentType.IsZeroSized)
                {
                    result.summary = "Zero-sized tag component.";
                    return result;
                }

                if (managedType == null)
                {
                    result.summary = "Managed type could not be resolved.";
                    return result;
                }

                if (componentType.IsBuffer)
                {
                    ReadBufferComponent(entityManager, entity, managedType, result);
                    return result;
                }

                object value = ReadComponentValue(entityManager, entity, componentType, managedType);
                result.summary = SafeFormatValue(value);
                result.fields = ReadInterestingFields(value, managedType);
            }
            catch (Exception ex)
            {
                result.error = $"{ex.GetType().Name}: {ex.Message}";
            }

            return result;
        }

        private static object ReadComponentValue(EntityManager entityManager, Entity entity, ComponentType componentType, Type managedType)
        {
            if (componentType.IsSharedComponent)
            {
                MethodInfo method = typeof(EntityManager)
                    .GetMethods()
                    .First(m => m.Name == nameof(EntityManager.GetSharedComponentData)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(Entity));
                return method.MakeGenericMethod(managedType).Invoke(entityManager, new object[] { entity });
            }

            if (componentType.IsManagedComponent)
            {
                MethodInfo method = typeof(EntityManager)
                    .GetMethods()
                    .First(m => m.Name == nameof(EntityManager.GetComponentObject)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(Entity));
                return method.MakeGenericMethod(managedType).Invoke(entityManager, new object[] { entity });
            }

            MethodInfo getComponentData = typeof(EntityManager)
                .GetMethods()
                .First(m => m.Name == nameof(EntityManager.GetComponentData)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Entity));
            return getComponentData.MakeGenericMethod(managedType).Invoke(entityManager, new object[] { entity });
        }

        private static void ReadBufferComponent(EntityManager entityManager, Entity entity, Type managedType, ComponentReadResult result)
        {
            MethodInfo getBuffer = typeof(EntityManager)
                .GetMethods()
                .First(m => m.Name == nameof(EntityManager.GetBuffer)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == typeof(Entity));
            object buffer = getBuffer.MakeGenericMethod(managedType).Invoke(entityManager, new object[] { entity, true });
            PropertyInfo lengthProperty = buffer.GetType().GetProperty("Length");
            int length = lengthProperty != null ? (int)lengthProperty.GetValue(buffer, null) : 0;
            result.bufferLength = length;
            result.summary = $"Dynamic buffer length: {length}";

            PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
            int sampleCount = Math.Min(length, MaxBufferElementSamples);

            for (int i = 0; i < sampleCount; i++)
            {
                try
                {
                    object element = itemProperty.GetValue(buffer, new object[] { i });
                    result.bufferSamples.Add(SummarizeObject(element, managedType));
                }
                catch (Exception ex)
                {
                    result.bufferSamples.Add($"Failed to read buffer element {i}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static List<FieldReadResult> ReadInterestingFields(object value, Type valueType)
        {
            List<FieldReadResult> fields = new List<FieldReadResult>();
            if (value == null || valueType == null)
            {
                return fields;
            }

            foreach (FieldInfo field in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!IsInterestingMember(field.Name) && fields.Count >= 4)
                {
                    continue;
                }

                fields.Add(ReadField(value, field));
                if (fields.Count >= MaxFieldReadsPerComponent)
                {
                    break;
                }
            }

            foreach (PropertyInfo property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (fields.Count >= MaxFieldReadsPerComponent)
                {
                    break;
                }

                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (!IsInterestingMember(property.Name) && fields.Count >= 4)
                {
                    continue;
                }

                fields.Add(ReadProperty(value, property));
            }

            return fields;
        }

        private static FieldReadResult ReadField(object value, FieldInfo field)
        {
            try
            {
                return new FieldReadResult { name = field.Name, value = SafeFormatValue(field.GetValue(value)) };
            }
            catch (Exception ex)
            {
                return new FieldReadResult { name = field.Name, value = $"Failed: {ex.GetType().Name}: {ex.Message}" };
            }
        }

        private static FieldReadResult ReadProperty(object value, PropertyInfo property)
        {
            try
            {
                return new FieldReadResult { name = property.Name, value = SafeFormatValue(property.GetValue(value, null)) };
            }
            catch (Exception ex)
            {
                return new FieldReadResult { name = property.Name, value = $"Failed: {ex.GetType().Name}: {ex.Message}" };
            }
        }

        private static string SummarizeObject(object value, Type valueType)
        {
            List<FieldReadResult> fields = ReadInterestingFields(value, valueType);
            if (fields.Count == 0)
            {
                return SafeFormatValue(value);
            }

            return string.Join(", ", fields.Select(field => $"{field.name}={field.value}").ToArray());
        }

        private static bool IsCandidateComponentType(ComponentType componentType)
        {
            string typeName = GetComponentTypeName(componentType);
            return GetMatchedKeywords(typeName).Any();
        }

        private static bool ShouldReadComponentDetails(ComponentType componentType)
        {
            string typeName = GetComponentTypeName(componentType);
            if (GetMatchedKeywords(typeName).Any())
            {
                return true;
            }

            if (ReadPriorityKeywords.Any(keyword => typeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            Type managedType = SafeGetManagedType(componentType);
            return managedType != null && HasInterestingMembers(managedType);
        }

        private static bool HasInterestingMembers(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public).Any(field => IsInterestingMember(field.Name))
                || type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Any(property => IsInterestingMember(property.Name));
        }

        private static bool IsInterestingMember(string memberName)
        {
            return ReadPriorityKeywords.Any(keyword => memberName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IEnumerable<string> GetMatchedKeywords(string text)
        {
            return CandidateKeywords.Where(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetComponentTypeName(ComponentType componentType)
        {
            Type managedType = SafeGetManagedType(componentType);
            return managedType != null ? managedType.FullName : componentType.ToString();
        }

        private static Type SafeGetManagedType(ComponentType componentType)
        {
            try
            {
                return componentType.GetManagedType();
            }
            catch
            {
                return null;
            }
        }

        private static string GetComponentCategory(ComponentType componentType)
        {
            if (componentType.IsBuffer)
            {
                return "Buffer";
            }

            if (componentType.IsSharedComponent)
            {
                return "SharedComponent";
            }

            if (componentType.IsManagedComponent)
            {
                return "ManagedComponent";
            }

            if (componentType.IsZeroSized)
            {
                return "Tag";
            }

            return "Component";
        }

        private static string SafeFormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            try
            {
                return value.ToString();
            }
            catch (Exception ex)
            {
                return $"Failed ToString: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private static void WriteJson(string jsonPath, TransportDebugDumpDocument document)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TransportDebugDumpDocument));
            using (FileStream stream = File.Create(jsonPath))
            {
                serializer.WriteObject(stream, document);
            }
        }

        private static string BuildTextReport(TransportDebugDumpDocument document)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("CS2 Metro Diagram - Transport Debug Dump");
            text.AppendLine($"Exported UTC: {document.exportedAtUtc}");
            text.AppendLine($"JSON: {document.jsonPath}");
            text.AppendLine($"TXT: {document.textPath}");
            text.AppendLine();
            text.AppendLine("Environment");
            text.AppendLine($"- Has update system: {document.environment.hasUpdateSystem}");
            text.AppendLine($"- Has world: {document.environment.hasWorld}");
            text.AppendLine($"- World created: {document.environment.worldIsCreated}");
            text.AppendLine($"- World name: {document.environment.worldName}");
            text.AppendLine($"- Game mode: {document.environment.gameMode}");
            text.AppendLine($"- Game state: {document.environment.gameState}");
            text.AppendLine($"- Should update world: {document.environment.shouldUpdateWorld}");
            text.AppendLine($"- Is game loading: {document.environment.isGameLoading}");
            text.AppendLine($"- Total entities: {document.environment.totalEntityCount}");
            text.AppendLine($"- Candidate entities: {document.environment.candidateEntityCount}");
            text.AppendLine($"- Candidate component types: {document.environment.candidateComponentTypeCount}");
            text.AppendLine();

            AppendTextList(text, "Warnings", document.warnings);
            AppendTextList(text, "Exceptions", document.exceptions);

            if (!string.IsNullOrWhiteSpace(document.routeGeometryDiagnostics))
            {
                text.AppendLine(document.routeGeometryDiagnostics);
                text.AppendLine();
            }

            text.AppendLine("Candidate Groups");
            foreach (TransportCandidateGroup group in document.candidateGroups)
            {
                text.AppendLine($"## {group.componentType}");
                text.AppendLine($"- Category: {group.componentCategory}");
                text.AppendLine($"- Total matching entities: {group.totalEntityCount}");
                text.AppendLine($"- Matched keywords: {string.Join(", ", group.matchedKeywords.ToArray())}");

                foreach (string error in group.errors)
                {
                    text.AppendLine($"- Error: {error}");
                }

                foreach (TransportEntitySample sample in group.samples)
                {
                    text.AppendLine($"  Entity {sample.entity} Name='{sample.entityName}'");
                    text.AppendLine($"  Components: {string.Join(", ", sample.componentTypes.ToArray())}");

                    foreach (ComponentReadResult component in sample.componentData)
                    {
                        text.AppendLine($"    Component: {component.componentType} [{component.category}] {component.summary}");
                        if (!string.IsNullOrWhiteSpace(component.error))
                        {
                            text.AppendLine($"      Error: {component.error}");
                        }

                        if (component.bufferLength.HasValue)
                        {
                            text.AppendLine($"      Buffer length: {component.bufferLength.Value}");
                        }

                        foreach (FieldReadResult field in component.fields)
                        {
                            text.AppendLine($"      {field.name}: {field.value}");
                        }

                        for (int i = 0; i < component.bufferSamples.Count; i++)
                        {
                            text.AppendLine($"      Buffer[{i}]: {component.bufferSamples[i]}");
                        }
                    }
                }

                text.AppendLine();
            }

            return text.ToString();
        }

        private static void AppendTextList(StringBuilder text, string title, List<string> items)
        {
            text.AppendLine(title);
            if (items.Count == 0)
            {
                text.AppendLine("- None");
            }
            else
            {
                foreach (string item in items)
                {
                    text.AppendLine($"- {item}");
                }
            }

            text.AppendLine();
        }
    }
}
