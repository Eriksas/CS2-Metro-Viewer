using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace CS2_Metro
{
    public static class MetroTrackGeometryDebugExporter
    {
        private const int MaxRouteSegmentSamplesPerLine = 10;
        private const int MaxReferencedEntitiesPerSegment = 80;
        private const int MaxReferencedEntityComponentsToRead = 12;
        private const int MaxSegmentComponentsToRead = 32;
        private const int MaxBufferSamplesPerComponent = 4;
        private const int MaxGeometryFieldsPerObject = 24;
        private const int MaxEntityReferencesPerObject = 64;

        private static readonly string[] GeometryKeywords =
        {
            "bezier",
            "curve",
            "position",
            "start",
            "end",
            "node",
            "edge",
            "lane",
            "track",
            "path",
            "segment",
            "target",
            "source",
            "owner"
        };

        private static readonly string[] CurveCandidateKeywords =
        {
            "bezier",
            "curve",
            "track",
            "lane",
            "edge",
            "path",
            "net"
        };

        public static string GetJsonPath()
        {
            return Path.Combine(TestMetroJsonExporter.GetDefaultExportDirectory(), "metro-track-geometry-debug.json");
        }

        public static string GetTextPath()
        {
            return Path.Combine(TestMetroJsonExporter.GetDefaultExportDirectory(), "metro-track-geometry-debug.txt");
        }

        public static bool Export(UpdateSystem updateSystem)
        {
            string jsonPath = GetJsonPath();
            string textPath = GetTextPath();
            Mod.log.Info($"Export Metro Track Geometry Debug started. JSON: {jsonPath}. TXT: {textPath}");

            try
            {
                Directory.CreateDirectory(TestMetroJsonExporter.GetDefaultExportDirectory());

                MetroTrackGeometryDebugDocument document = CreateDocument(updateSystem, jsonPath, textPath);
                WriteJson(jsonPath, document);
                File.WriteAllText(textPath, BuildTextReport(document), new UTF8Encoding(false));

                Mod.log.Info($"Export Metro Track Geometry Debug succeeded. JSON: {jsonPath}. TXT: {textPath}");
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Export Metro Track Geometry Debug failed. JSON: {jsonPath}. TXT: {textPath}. Error: {ex}");

                try
                {
                    File.WriteAllText(textPath, "Export Metro Track Geometry Debug failed." + Environment.NewLine + ex, new UTF8Encoding(false));
                }
                catch
                {
                }

                return false;
            }
        }

        private static MetroTrackGeometryDebugDocument CreateDocument(UpdateSystem updateSystem, string jsonPath, string textPath)
        {
            MetroTrackGeometryDebugDocument document = new MetroTrackGeometryDebugDocument
            {
                version = VersionInfo.ReleaseVersion,
                exportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                jsonPath = jsonPath,
                textPath = textPath,
                maxRouteSegmentSamplesPerLine = MaxRouteSegmentSamplesPerLine
            };

            if (updateSystem == null)
            {
                document.warnings.Add("UpdateSystem is null. No ECS data was scanned.");
                document.summary.recoverability = "not evaluated; UpdateSystem is null";
                return document;
            }

            World world = null;
            try
            {
                world = updateSystem.World;
                document.environment.hasWorld = world != null;
                document.environment.worldIsCreated = world != null && world.IsCreated;
                document.environment.worldName = world != null ? world.Name : string.Empty;
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Failed to read UpdateSystem.World: {ex.GetType().Name}: {ex.Message}");
            }

            if (world == null || !world.IsCreated)
            {
                document.warnings.Add("World is unavailable or not created. No ECS data was scanned.");
                document.summary.recoverability = "not evaluated; world is unavailable";
                return document;
            }

            NameSystem nameSystem = TryGetNameSystem(world, document);
            EntityManager entityManager = updateSystem.EntityManager;

            try
            {
                using (NativeArray<Entity> allEntities = entityManager.GetAllEntities(Allocator.Temp))
                {
                    document.environment.totalEntityCount = allEntities.Length;
                }
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Failed to count entities: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                ScanSubwayLines(entityManager, nameSystem, document);
            }
            catch (Exception ex)
            {
                document.exceptions.Add($"Metro track geometry scan failed: {ex}");
                Mod.log.Error($"Metro track geometry scan failed: {ex}");
            }

            FinalizeSummary(document);
            return document;
        }

        private static NameSystem TryGetNameSystem(World world, MetroTrackGeometryDebugDocument document)
        {
            try
            {
                return world.GetExistingSystemManaged<NameSystem>();
            }
            catch (Exception ex)
            {
                document.warnings.Add($"NameSystem unavailable; fallback line names may be used. {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void ScanSubwayLines(EntityManager entityManager, NameSystem nameSystem, MetroTrackGeometryDebugDocument document)
        {
            using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TransportLine>()))
            using (NativeArray<Entity> lineEntities = query.ToEntityArray(Allocator.Temp))
            {
                document.summary.transportLineCount = lineEntities.Length;

                for (int i = 0; i < lineEntities.Length; i++)
                {
                    Entity lineEntity = lineEntities[i];
                    string subwayReason;
                    if (!IsSubwayLine(entityManager, lineEntity, out subwayReason))
                    {
                        continue;
                    }

                    MetroTrackGeometryLineDebug line = CreateLineDebug(entityManager, nameSystem, lineEntity, i, subwayReason);
                    document.lines.Add(line);
                    document.summary.subwayLineCount++;
                    document.summary.totalRouteSegments += line.routeSegmentCount;
                    document.summary.totalSampledSegments += line.sampledSegmentCount;
                    document.summary.totalReferencedGameNetEntities += line.referencedGameNetEntityCount;
                    document.summary.totalGeometryLikeFields += line.geometryLikeFieldCount;
                }
            }
        }

        private static MetroTrackGeometryLineDebug CreateLineDebug(EntityManager entityManager, NameSystem nameSystem, Entity lineEntity, int lineIndex, string subwayReason)
        {
            MetroTrackGeometryLineDebug line = new MetroTrackGeometryLineDebug
            {
                lineEntityId = FormatEntity(lineEntity),
                lineName = ReadLineName(entityManager, nameSystem, lineEntity, lineIndex),
                routeNumber = ReadRouteNumber(entityManager, lineEntity),
                waypointCount = GetWaypointCount(entityManager, lineEntity),
                subwayMatchReason = subwayReason
            };

            try
            {
                ComponentType routeSegmentBufferType;
                Type routeSegmentElementType;
                if (!TryFindRouteSegmentBufferType(entityManager, lineEntity, out routeSegmentBufferType, out routeSegmentElementType))
                {
                    line.hasRouteSegmentBuffer = false;
                    line.warnings.Add("RouteSegment buffer was not found on this subway TransportLine.");
                    return line;
                }

                line.hasRouteSegmentBuffer = true;
                object buffer = ReadBuffer(entityManager, lineEntity, routeSegmentElementType);
                line.routeSegmentCount = ReadBufferLength(buffer);
                line.sampledSegmentCount = Math.Min(line.routeSegmentCount, MaxRouteSegmentSamplesPerLine);

                PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
                if (itemProperty == null)
                {
                    line.warnings.Add("RouteSegment buffer item reader unavailable.");
                    return line;
                }

                HashSet<string> gameNetEntityKeys = new HashSet<string>();
                HashSet<string> candidateSet = new HashSet<string>();

                for (int i = 0; i < line.sampledSegmentCount; i++)
                {
                    MetroTrackGeometrySegmentDebug segment = CreateSegmentDebug(entityManager, buffer, itemProperty, routeSegmentElementType, i, gameNetEntityKeys, candidateSet);
                    line.segments.Add(segment);
                    line.geometryLikeFieldCount += segment.geometryLikeFields.Count;
                    foreach (MetroTrackGeometryReferencedEntity referencedEntity in segment.referencedEntities)
                    {
                        line.geometryLikeFieldCount += referencedEntity.geometryLikeFields.Count;
                    }
                }

                line.referencedGameNetEntityCount = gameNetEntityKeys.Count;
                line.likelyCurveSourceCandidates = candidateSet.OrderBy(value => value).ToList();
            }
            catch (Exception ex)
            {
                line.exceptions.Add($"RouteSegment scan failed: {ex.GetType().Name}: {ex.Message}");
            }

            return line;
        }

        private static MetroTrackGeometrySegmentDebug CreateSegmentDebug(EntityManager entityManager, object buffer, PropertyInfo itemProperty, Type routeSegmentElementType, int segmentIndex, HashSet<string> gameNetEntityKeys, HashSet<string> candidateSet)
        {
            MetroTrackGeometrySegmentDebug segment = new MetroTrackGeometrySegmentDebug
            {
                segmentIndex = segmentIndex
            };

            try
            {
                object segmentElement = itemProperty.GetValue(buffer, new object[] { segmentIndex });
                segment.entityReferenceFields = FindEntityReferences(segmentElement, routeSegmentElementType, "RouteSegment")
                    .Take(MaxReferencedEntitiesPerSegment)
                    .Select(reference => CreateEntityReference(reference))
                    .ToList();
                segment.geometryLikeFields = ReadGeometryLikeFields(segmentElement, routeSegmentElementType, "RouteSegment", MaxGeometryFieldsPerObject);
                AddCurveCandidates(candidateSet, "RouteSegment", routeSegmentElementType, segment.geometryLikeFields);

                Entity segmentEntity = ChooseSegmentEntity(entityManager, segment.entityReferenceFields);
                segment.segmentEntityId = IsValidEntity(entityManager, segmentEntity) ? FormatEntity(segmentEntity) : "unavailable";

                if (!IsValidEntity(entityManager, segmentEntity))
                {
                    segment.warnings.Add("No valid RouteSegment entity reference could be identified from the buffer item.");
                    return segment;
                }

                List<ComponentType> componentTypes = GetComponentTypes(entityManager, segmentEntity);
                segment.componentTypes = componentTypes.Select(GetComponentTypeName).OrderBy(name => name).ToList();
                AddCurveCandidates(candidateSet, segment.segmentEntityId, segment.componentTypes);

                ReadSegmentEntityComponents(entityManager, segmentEntity, componentTypes, segment, candidateSet);
                DumpReferencedEntities(entityManager, segment, gameNetEntityKeys, candidateSet);
            }
            catch (Exception ex)
            {
                segment.exceptions.Add($"Segment sample failed: {ex.GetType().Name}: {ex.Message}");
            }

            return segment;
        }

        private static void ReadSegmentEntityComponents(EntityManager entityManager, Entity segmentEntity, List<ComponentType> componentTypes, MetroTrackGeometrySegmentDebug segment, HashSet<string> candidateSet)
        {
            int componentsRead = 0;

            foreach (ComponentType componentType in componentTypes)
            {
                if (componentsRead >= MaxSegmentComponentsToRead)
                {
                    segment.warnings.Add($"Segment component read limit reached at {MaxSegmentComponentsToRead} components.");
                    return;
                }

                Type managedType = SafeGetManagedType(componentType);
                string typeName = managedType != null ? managedType.FullName : componentType.ToString();
                if (managedType == null)
                {
                    continue;
                }

                if (!ShouldInspectComponent(typeName, managedType))
                {
                    continue;
                }

                componentsRead++;
                AddCurveCandidate(candidateSet, typeName);

                try
                {
                    if (componentType.IsZeroSized)
                    {
                        continue;
                    }

                    if (componentType.IsBuffer)
                    {
                        ReadBufferComponentForSegment(entityManager, segmentEntity, managedType, typeName, segment, candidateSet);
                    }
                    else
                    {
                        object value = ReadComponentValue(entityManager, segmentEntity, componentType, managedType);
                        MergeReferences(segment.entityReferenceFields, FindEntityReferences(value, managedType, typeName));
                        List<MetroTrackGeometryField> fields = ReadGeometryLikeFields(value, managedType, typeName, MaxGeometryFieldsPerObject);
                        segment.geometryLikeFields.AddRange(fields);
                        AddCurveCandidates(candidateSet, typeName, managedType, fields);
                    }
                }
                catch (Exception ex)
                {
                    segment.exceptions.Add($"Failed reading segment component {typeName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void ReadBufferComponentForSegment(EntityManager entityManager, Entity segmentEntity, Type managedType, string typeName, MetroTrackGeometrySegmentDebug segment, HashSet<string> candidateSet)
        {
            object buffer = ReadBuffer(entityManager, segmentEntity, managedType);
            int length = ReadBufferLength(buffer);
            PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
            int sampleCount = Math.Min(length, MaxBufferSamplesPerComponent);

            if (sampleCount == 0)
            {
                return;
            }

            if (itemProperty == null)
            {
                segment.exceptions.Add($"Buffer item reader unavailable for {typeName}.");
                return;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                try
                {
                    string prefix = $"{typeName}[{i}]";
                    object element = itemProperty.GetValue(buffer, new object[] { i });
                    MergeReferences(segment.entityReferenceFields, FindEntityReferences(element, managedType, prefix));
                    List<MetroTrackGeometryField> fields = ReadGeometryLikeFields(element, managedType, prefix, MaxGeometryFieldsPerObject);
                    segment.geometryLikeFields.AddRange(fields);
                    AddCurveCandidates(candidateSet, prefix, managedType, fields);
                }
                catch (Exception ex)
                {
                    segment.exceptions.Add($"Failed reading {typeName}[{i}]: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void DumpReferencedEntities(EntityManager entityManager, MetroTrackGeometrySegmentDebug segment, HashSet<string> gameNetEntityKeys, HashSet<string> candidateSet)
        {
            Dictionary<string, MetroTrackGeometryEntityReference> distinctReferences = new Dictionary<string, MetroTrackGeometryEntityReference>();
            foreach (MetroTrackGeometryEntityReference reference in segment.entityReferenceFields)
            {
                string key = $"{reference.fieldPath}|{reference.entityId}";
                if (!distinctReferences.ContainsKey(key))
                {
                    distinctReferences.Add(key, reference);
                }
            }

            foreach (MetroTrackGeometryEntityReference reference in distinctReferences.Values.Take(MaxReferencedEntitiesPerSegment))
            {
                Entity entity = reference.Entity;
                if (!IsValidEntity(entityManager, entity))
                {
                    continue;
                }

                MetroTrackGeometryReferencedEntity referencedEntity = CreateReferencedEntityDebug(entityManager, reference, candidateSet);
                if (!referencedEntity.isGameNetEntity)
                {
                    continue;
                }

                gameNetEntityKeys.Add(referencedEntity.entityId);
                segment.referencedEntities.Add(referencedEntity);
            }
        }

        private static MetroTrackGeometryReferencedEntity CreateReferencedEntityDebug(EntityManager entityManager, MetroTrackGeometryEntityReference reference, HashSet<string> candidateSet)
        {
            MetroTrackGeometryReferencedEntity referencedEntity = new MetroTrackGeometryReferencedEntity
            {
                sourceFieldPath = reference.fieldPath,
                entityId = reference.entityId
            };

            try
            {
                List<ComponentType> componentTypes = GetComponentTypes(entityManager, reference.Entity);
                referencedEntity.componentTypes = componentTypes.Select(GetComponentTypeName).OrderBy(name => name).ToList();
                referencedEntity.gameNetComponentTypes = referencedEntity.componentTypes
                    .Where(IsGameNetComponentType)
                    .OrderBy(name => name)
                    .ToList();
                referencedEntity.isGameNetEntity = referencedEntity.gameNetComponentTypes.Count > 0;

                if (!referencedEntity.isGameNetEntity)
                {
                    return referencedEntity;
                }

                AddCurveCandidates(candidateSet, reference.entityId, referencedEntity.gameNetComponentTypes);

                int componentsRead = 0;
                foreach (ComponentType componentType in componentTypes)
                {
                    if (componentsRead >= MaxReferencedEntityComponentsToRead)
                    {
                        referencedEntity.warnings.Add($"Referenced entity component read limit reached at {MaxReferencedEntityComponentsToRead} components.");
                        break;
                    }

                    Type managedType = SafeGetManagedType(componentType);
                    string typeName = managedType != null ? managedType.FullName : componentType.ToString();
                    if (managedType == null || componentType.IsZeroSized || !ShouldInspectComponent(typeName, managedType))
                    {
                        continue;
                    }

                    componentsRead++;
                    AddCurveCandidate(candidateSet, typeName);

                    try
                    {
                        if (componentType.IsBuffer)
                        {
                            ReadBufferGeometryFields(entityManager, reference.Entity, managedType, typeName, referencedEntity.geometryLikeFields, candidateSet);
                        }
                        else
                        {
                            object value = ReadComponentValue(entityManager, reference.Entity, componentType, managedType);
                            List<MetroTrackGeometryField> fields = ReadGeometryLikeFields(value, managedType, typeName, MaxGeometryFieldsPerObject);
                            referencedEntity.geometryLikeFields.AddRange(fields);
                            AddCurveCandidates(candidateSet, typeName, managedType, fields);
                        }
                    }
                    catch (Exception ex)
                    {
                        referencedEntity.exceptions.Add($"Failed reading {typeName}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                referencedEntity.exceptions.Add($"Referenced entity dump failed: {ex.GetType().Name}: {ex.Message}");
            }

            return referencedEntity;
        }

        private static void ReadBufferGeometryFields(EntityManager entityManager, Entity entity, Type managedType, string typeName, List<MetroTrackGeometryField> destination, HashSet<string> candidateSet)
        {
            object buffer = ReadBuffer(entityManager, entity, managedType);
            int length = ReadBufferLength(buffer);
            PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
            int sampleCount = Math.Min(length, MaxBufferSamplesPerComponent);

            if (itemProperty == null)
            {
                return;
            }

            for (int i = 0; i < sampleCount; i++)
            {
                object element = itemProperty.GetValue(buffer, new object[] { i });
                string prefix = $"{typeName}[{i}]";
                List<MetroTrackGeometryField> fields = ReadGeometryLikeFields(element, managedType, prefix, MaxGeometryFieldsPerObject);
                destination.AddRange(fields);
                AddCurveCandidates(candidateSet, prefix, managedType, fields);
            }
        }

        private static void FinalizeSummary(MetroTrackGeometryDebugDocument document)
        {
            HashSet<string> candidates = new HashSet<string>();
            foreach (MetroTrackGeometryLineDebug line in document.lines)
            {
                foreach (string candidate in line.likelyCurveSourceCandidates)
                {
                    candidates.Add(candidate);
                }
            }

            document.summary.likelyCurveSourceCandidates = candidates.OrderBy(value => value).Take(80).ToList();
            document.summary.recoverability = document.summary.totalReferencedGameNetEntities > 0 || document.summary.totalGeometryLikeFields > 0
                ? "likely recoverable; inspect likelyCurveSourceCandidates and referenced Game.Net entities"
                : "not proven; sampled RouteSegments did not expose Game.Net references or geometry-like fields";
        }

        private static bool TryFindRouteSegmentBufferType(EntityManager entityManager, Entity lineEntity, out ComponentType routeSegmentBufferType, out Type routeSegmentElementType)
        {
            routeSegmentBufferType = default(ComponentType);
            routeSegmentElementType = null;

            foreach (ComponentType componentType in GetComponentTypes(entityManager, lineEntity))
            {
                if (!componentType.IsBuffer)
                {
                    continue;
                }

                Type managedType = SafeGetManagedType(componentType);
                string typeName = managedType != null ? managedType.FullName : componentType.ToString();
                if (typeName != null && typeName.IndexOf("RouteSegment", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    routeSegmentBufferType = componentType;
                    routeSegmentElementType = managedType;
                    return routeSegmentElementType != null;
                }
            }

            return false;
        }

        private static object ReadBuffer(EntityManager entityManager, Entity entity, Type managedType)
        {
            MethodInfo getBuffer = typeof(EntityManager)
                .GetMethods()
                .First(m => m.Name == nameof(EntityManager.GetBuffer)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType == typeof(Entity));
            return getBuffer.MakeGenericMethod(managedType).Invoke(entityManager, new object[] { entity, true });
        }

        private static int ReadBufferLength(object buffer)
        {
            if (buffer == null)
            {
                return 0;
            }

            PropertyInfo lengthProperty = buffer.GetType().GetProperty("Length");
            object value = lengthProperty != null ? lengthProperty.GetValue(buffer, null) : null;
            return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
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

        private static List<MetroTrackGeometryField> ReadGeometryLikeFields(object value, Type valueType, string prefix, int maxFields)
        {
            List<MetroTrackGeometryField> fields = new List<MetroTrackGeometryField>();
            if (value == null || valueType == null)
            {
                return fields;
            }

            CollectGeometryLikeFields(value, valueType, prefix, fields, maxFields, 0);
            return fields;
        }

        private static void CollectGeometryLikeFields(object value, Type valueType, string path, List<MetroTrackGeometryField> fields, int maxFields, int depth)
        {
            if (value == null || valueType == null || fields.Count >= maxFields || depth > 3 || !ShouldInspectNestedType(valueType))
            {
                return;
            }

            foreach (FieldInfo field in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (fields.Count >= maxFields)
                {
                    return;
                }

                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch (Exception ex)
                {
                    if (IsGeometryLikeMember(field.Name))
                    {
                        fields.Add(new MetroTrackGeometryField { fieldPath = CombinePath(path, field.Name), typeName = field.FieldType.FullName, valueSummary = $"Failed: {ex.GetType().Name}: {ex.Message}" });
                    }

                    continue;
                }

                string childPath = CombinePath(path, field.Name);
                if (IsGeometryLikeMember(field.Name) || IsGeometryLikeType(field.FieldType))
                {
                    fields.Add(new MetroTrackGeometryField { fieldPath = childPath, typeName = field.FieldType.FullName, valueSummary = SafeFormatValue(fieldValue) });
                }

                if (depth < 3 && ShouldInspectNestedType(field.FieldType))
                {
                    CollectGeometryLikeFields(fieldValue, field.FieldType, childPath, fields, maxFields, depth + 1);
                }
            }

            foreach (PropertyInfo property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (fields.Count >= maxFields)
                {
                    return;
                }

                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object propertyValue;
                try
                {
                    propertyValue = property.GetValue(value, null);
                }
                catch (Exception ex)
                {
                    if (IsGeometryLikeMember(property.Name))
                    {
                        fields.Add(new MetroTrackGeometryField { fieldPath = CombinePath(path, property.Name), typeName = property.PropertyType.FullName, valueSummary = $"Failed: {ex.GetType().Name}: {ex.Message}" });
                    }

                    continue;
                }

                string childPath = CombinePath(path, property.Name);
                if (IsGeometryLikeMember(property.Name) || IsGeometryLikeType(property.PropertyType))
                {
                    fields.Add(new MetroTrackGeometryField { fieldPath = childPath, typeName = property.PropertyType.FullName, valueSummary = SafeFormatValue(propertyValue) });
                }

                if (depth < 3 && ShouldInspectNestedType(property.PropertyType))
                {
                    CollectGeometryLikeFields(propertyValue, property.PropertyType, childPath, fields, maxFields, depth + 1);
                }
            }
        }

        private static List<MetroTrackGeometryEntityReference> FindEntityReferences(object value, Type valueType, string prefix)
        {
            List<MetroTrackGeometryEntityReference> references = new List<MetroTrackGeometryEntityReference>();
            CollectEntityReferences(value, valueType, prefix, references, 0);

            Dictionary<string, MetroTrackGeometryEntityReference> distinct = new Dictionary<string, MetroTrackGeometryEntityReference>();
            foreach (MetroTrackGeometryEntityReference reference in references)
            {
                string key = $"{reference.fieldPath}|{reference.entityId}";
                if (!distinct.ContainsKey(key))
                {
                    distinct.Add(key, reference);
                }
            }

            return distinct.Values.Take(MaxEntityReferencesPerObject).ToList();
        }

        private static void CollectEntityReferences(object value, Type valueType, string path, List<MetroTrackGeometryEntityReference> references, int depth)
        {
            if (value == null || valueType == null || references.Count >= MaxEntityReferencesPerObject || depth > 3)
            {
                return;
            }

            if (valueType == typeof(Entity))
            {
                references.Add(CreateEntityReference(new MetroTrackGeometryEntityReference { fieldPath = path, Entity = (Entity)value, entityId = FormatEntity((Entity)value) }));
                return;
            }

            if (!ShouldInspectNestedType(valueType))
            {
                return;
            }

            foreach (FieldInfo field in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object fieldValue;
                try
                {
                    fieldValue = field.GetValue(value);
                }
                catch
                {
                    continue;
                }

                string childPath = CombinePath(path, field.Name);
                if (field.FieldType == typeof(Entity))
                {
                    references.Add(new MetroTrackGeometryEntityReference { fieldPath = childPath, entityId = FormatEntity((Entity)fieldValue), Entity = (Entity)fieldValue });
                }
                else if (depth < 3 && ShouldInspectNestedType(field.FieldType))
                {
                    CollectEntityReferences(fieldValue, field.FieldType, childPath, references, depth + 1);
                }
            }

            foreach (PropertyInfo property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object propertyValue;
                try
                {
                    propertyValue = property.GetValue(value, null);
                }
                catch
                {
                    continue;
                }

                string childPath = CombinePath(path, property.Name);
                if (property.PropertyType == typeof(Entity))
                {
                    references.Add(new MetroTrackGeometryEntityReference { fieldPath = childPath, entityId = FormatEntity((Entity)propertyValue), Entity = (Entity)propertyValue });
                }
                else if (depth < 3 && ShouldInspectNestedType(property.PropertyType))
                {
                    CollectEntityReferences(propertyValue, property.PropertyType, childPath, references, depth + 1);
                }
            }
        }

        private static MetroTrackGeometryEntityReference CreateEntityReference(MetroTrackGeometryEntityReference reference)
        {
            return new MetroTrackGeometryEntityReference
            {
                fieldPath = reference.fieldPath,
                entityId = reference.entityId,
                Entity = reference.Entity
            };
        }

        private static void MergeReferences(List<MetroTrackGeometryEntityReference> destination, List<MetroTrackGeometryEntityReference> source)
        {
            HashSet<string> existing = new HashSet<string>(destination.Select(reference => $"{reference.fieldPath}|{reference.entityId}"));
            foreach (MetroTrackGeometryEntityReference reference in source)
            {
                string key = $"{reference.fieldPath}|{reference.entityId}";
                if (!existing.Contains(key))
                {
                    destination.Add(reference);
                    existing.Add(key);
                }
            }
        }

        private static Entity ChooseSegmentEntity(EntityManager entityManager, List<MetroTrackGeometryEntityReference> references)
        {
            Entity fallback = Entity.Null;
            foreach (MetroTrackGeometryEntityReference reference in references)
            {
                if (!IsValidEntity(entityManager, reference.Entity))
                {
                    continue;
                }

                if (reference.fieldPath.IndexOf("segment", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return reference.Entity;
                }

                if (fallback == Entity.Null)
                {
                    fallback = reference.Entity;
                }
            }

            return fallback;
        }

        private static bool IsSubwayLine(EntityManager entityManager, Entity lineEntity, out string reason)
        {
            List<string> checks = new List<string>();

            try
            {
                if (entityManager.HasComponent<PrefabRef>(lineEntity))
                {
                    Entity prefab = entityManager.GetComponentData<PrefabRef>(lineEntity).m_Prefab;
                    if (IsValidEntity(entityManager, prefab)
                        && entityManager.HasComponent<TransportLineData>(prefab)
                        && entityManager.GetComponentData<TransportLineData>(prefab).m_TransportType == TransportType.Subway)
                    {
                        reason = $"TransportLineData prefab {FormatEntity(prefab)} transport type Subway";
                        return true;
                    }

                    checks.Add($"TransportLineData prefab {FormatEntity(prefab)} not Subway/unreadable");
                }
            }
            catch (Exception ex)
            {
                checks.Add($"PrefabRef check failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                if (entityManager.HasBuffer<VehicleModel>(lineEntity))
                {
                    DynamicBuffer<VehicleModel> models = entityManager.GetBuffer<VehicleModel>(lineEntity, true);
                    if (models.Length > 0)
                    {
                        Entity primaryPrefab = models[0].m_PrimaryPrefab;
                        if (IsValidEntity(entityManager, primaryPrefab)
                            && entityManager.HasComponent<PublicTransportVehicleData>(primaryPrefab)
                            && entityManager.GetComponentData<PublicTransportVehicleData>(primaryPrefab).m_TransportType == TransportType.Subway)
                        {
                            reason = $"VehicleModel[0].m_PrimaryPrefab {FormatEntity(primaryPrefab)} transport type Subway";
                            return true;
                        }

                        checks.Add($"VehicleModel[0] primary prefab {FormatEntity(primaryPrefab)} not Subway/unreadable");
                    }
                }
            }
            catch (Exception ex)
            {
                checks.Add($"VehicleModel check failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                if (entityManager.HasBuffer<RouteWaypoint>(lineEntity))
                {
                    DynamicBuffer<RouteWaypoint> waypoints = entityManager.GetBuffer<RouteWaypoint>(lineEntity, true);
                    for (int i = 0; i < waypoints.Length; i++)
                    {
                        Entity waypoint = waypoints[i].m_Waypoint;
                        Entity connected = ReadConnectedEntity(entityManager, waypoint);
                        if (IsValidEntity(entityManager, connected) && entityManager.HasComponent<SubwayStop>(connected))
                        {
                            reason = $"RouteWaypoint[{i}] connected to SubwayStop {FormatEntity(connected)}";
                            return true;
                        }
                    }

                    checks.Add("No waypoint connected to SubwayStop");
                }
            }
            catch (Exception ex)
            {
                checks.Add($"Waypoint SubwayStop check failed: {ex.GetType().Name}: {ex.Message}");
            }

            reason = string.Join("; ", checks.ToArray());
            return false;
        }

        private static Entity ReadConnectedEntity(EntityManager entityManager, Entity waypointEntity)
        {
            try
            {
                if (IsValidEntity(entityManager, waypointEntity) && entityManager.HasComponent<Connected>(waypointEntity))
                {
                    return entityManager.GetComponentData<Connected>(waypointEntity).m_Connected;
                }
            }
            catch
            {
            }

            return Entity.Null;
        }

        private static string ReadLineName(EntityManager entityManager, NameSystem nameSystem, Entity lineEntity, int lineIndex)
        {
            try
            {
                if (nameSystem != null)
                {
                    string renderedName = nameSystem.GetRenderedLabelName(lineEntity);
                    if (!string.IsNullOrWhiteSpace(renderedName))
                    {
                        return renderedName;
                    }
                }
            }
            catch
            {
            }

            try
            {
                string entityName = entityManager.GetName(lineEntity);
                if (!string.IsNullOrWhiteSpace(entityName))
                {
                    return entityName;
                }
            }
            catch
            {
            }

            int routeNumber = ReadRouteNumber(entityManager, lineEntity);
            return routeNumber > 0 ? $"Metro Line {routeNumber}" : $"Metro Line {lineIndex + 1}";
        }

        private static int ReadRouteNumber(EntityManager entityManager, Entity lineEntity)
        {
            try
            {
                return entityManager.HasComponent<RouteNumber>(lineEntity)
                    ? entityManager.GetComponentData<RouteNumber>(lineEntity).m_Number
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int GetWaypointCount(EntityManager entityManager, Entity lineEntity)
        {
            try
            {
                return entityManager.HasBuffer<RouteWaypoint>(lineEntity)
                    ? entityManager.GetBuffer<RouteWaypoint>(lineEntity, true).Length
                    : 0;
            }
            catch
            {
                return 0;
            }
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

        private static string GetComponentTypeName(ComponentType componentType)
        {
            Type managedType = SafeGetManagedType(componentType);
            return managedType != null ? managedType.FullName : componentType.ToString();
        }

        private static bool ShouldInspectComponent(string typeName, Type managedType)
        {
            return IsGameNetComponentType(typeName)
                || IsCurveCandidateText(typeName)
                || HasGeometryLikeMembers(managedType);
        }

        private static bool HasGeometryLikeMembers(Type type)
        {
            return type != null
                && (type.GetFields(BindingFlags.Instance | BindingFlags.Public).Any(field => IsGeometryLikeMember(field.Name) || IsGeometryLikeType(field.FieldType))
                    || type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Any(property => property.CanRead && property.GetIndexParameters().Length == 0 && (IsGeometryLikeMember(property.Name) || IsGeometryLikeType(property.PropertyType))));
        }

        private static bool ShouldInspectNestedType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return false;
            }

            return type.IsValueType || (type.Namespace != null && (type.Namespace.StartsWith("Game.", StringComparison.Ordinal) || type.Namespace.StartsWith("Unity.", StringComparison.Ordinal)));
        }

        private static bool IsGeometryLikeMember(string memberName)
        {
            return memberName != null && GeometryKeywords.Any(keyword => memberName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGeometryLikeType(Type type)
        {
            string typeName = type != null ? type.FullName : string.Empty;
            return IsCurveCandidateText(typeName);
        }

        private static bool IsCurveCandidateText(string text)
        {
            return text != null && CurveCandidateKeywords.Any(keyword => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGameNetComponentType(string typeName)
        {
            return typeName != null && typeName.IndexOf("Game.Net.", StringComparison.Ordinal) >= 0;
        }

        private static bool IsValidEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null && entity.Index >= 0 && entityManager.Exists(entity);
        }

        private static void AddCurveCandidates(HashSet<string> destination, string prefix, Type type, List<MetroTrackGeometryField> fields)
        {
            AddCurveCandidate(destination, type != null ? type.FullName : string.Empty);

            foreach (MetroTrackGeometryField field in fields)
            {
                if (IsCurveCandidateText(field.fieldPath) || IsCurveCandidateText(field.typeName))
                {
                    AddCurveCandidate(destination, $"{prefix}: {field.fieldPath} ({field.typeName})");
                }
            }
        }

        private static void AddCurveCandidates(HashSet<string> destination, string prefix, List<string> typeNames)
        {
            foreach (string typeName in typeNames)
            {
                if (IsCurveCandidateText(typeName))
                {
                    AddCurveCandidate(destination, $"{prefix}: {typeName}");
                }
            }
        }

        private static void AddCurveCandidate(HashSet<string> destination, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && IsCurveCandidateText(value))
            {
                destination.Add(value);
            }
        }

        private static string CombinePath(string parent, string child)
        {
            return string.IsNullOrWhiteSpace(parent) ? child : $"{parent}.{child}";
        }

        private static string SafeFormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is Entity)
            {
                return FormatEntity((Entity)value);
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

        private static string FormatEntity(Entity entity)
        {
            return $"{entity.Index}:{entity.Version}";
        }

        private static void WriteJson(string jsonPath, MetroTrackGeometryDebugDocument document)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MetroTrackGeometryDebugDocument));
            using (FileStream stream = File.Create(jsonPath))
            {
                serializer.WriteObject(stream, document);
            }
        }

        private static string BuildTextReport(MetroTrackGeometryDebugDocument document)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("CS2 Metro Diagram - Metro Track Geometry Debug");
            text.AppendLine($"Exported UTC: {document.exportedAtUtc}");
            text.AppendLine($"JSON: {document.jsonPath}");
            text.AppendLine($"TXT: {document.textPath}");
            text.AppendLine();
            text.AppendLine("Summary");
            text.AppendLine($"- TransportLine count: {document.summary.transportLineCount}");
            text.AppendLine($"- Subway line count: {document.summary.subwayLineCount}");
            text.AppendLine($"- Total route segments: {document.summary.totalRouteSegments}");
            text.AppendLine($"- Total sampled segments: {document.summary.totalSampledSegments}");
            text.AppendLine($"- Referenced Game.Net entity count: {document.summary.totalReferencedGameNetEntities}");
            text.AppendLine($"- Geometry-like field count: {document.summary.totalGeometryLikeFields}");
            text.AppendLine($"- Recoverability: {document.summary.recoverability}");
            text.AppendLine("- Likely curve source candidates:");
            AppendTextList(text, document.summary.likelyCurveSourceCandidates);
            text.AppendLine();

            AppendNamedTextList(text, "Warnings", document.warnings);
            AppendNamedTextList(text, "Exceptions", document.exceptions);

            foreach (MetroTrackGeometryLineDebug line in document.lines)
            {
                text.AppendLine($"Line {line.lineName} ({line.lineEntityId})");
                text.AppendLine($"- Route number: {(line.routeNumber > 0 ? line.routeNumber.ToString(CultureInfo.InvariantCulture) : "unavailable")}");
                text.AppendLine($"- Waypoint count: {line.waypointCount}");
                text.AppendLine($"- RouteSegment buffer exists: {line.hasRouteSegmentBuffer}");
                text.AppendLine($"- Route segment count: {line.routeSegmentCount}");
                text.AppendLine($"- Sampled segment count: {line.sampledSegmentCount}");
                text.AppendLine($"- Referenced Game.Net entity count: {line.referencedGameNetEntityCount}");
                text.AppendLine($"- Geometry-like field count: {line.geometryLikeFieldCount}");
                text.AppendLine($"- Subway match: {line.subwayMatchReason}");
                text.AppendLine("- Likely curve source candidates:");
                AppendTextList(text, line.likelyCurveSourceCandidates);

                foreach (MetroTrackGeometrySegmentDebug segment in line.segments)
                {
                    text.AppendLine($"  Segment sample[{segment.segmentIndex}]");
                    text.AppendLine($"  - Segment entity id: {segment.segmentEntityId}");
                    text.AppendLine($"  - Component types: {JoinOrNone(segment.componentTypes)}");
                    text.AppendLine("  - Entity reference fields:");
                    foreach (MetroTrackGeometryEntityReference reference in segment.entityReferenceFields)
                    {
                        text.AppendLine($"    - {reference.fieldPath}: {reference.entityId}");
                    }

                    if (segment.entityReferenceFields.Count == 0)
                    {
                        text.AppendLine("    - none");
                    }

                    text.AppendLine("  - Geometry-like fields:");
                    AppendFieldList(text, segment.geometryLikeFields, "    ");
                    text.AppendLine("  - Referenced Game.Net entities:");
                    foreach (MetroTrackGeometryReferencedEntity referencedEntity in segment.referencedEntities)
                    {
                        text.AppendLine($"    - {referencedEntity.sourceFieldPath}: {referencedEntity.entityId}");
                        text.AppendLine($"      Components: {JoinOrNone(referencedEntity.componentTypes)}");
                        text.AppendLine($"      Game.Net components: {JoinOrNone(referencedEntity.gameNetComponentTypes)}");
                        text.AppendLine("      Geometry-like fields:");
                        AppendFieldList(text, referencedEntity.geometryLikeFields, "        ");
                    }

                    if (segment.referencedEntities.Count == 0)
                    {
                        text.AppendLine("    - none");
                    }

                    AppendNamedTextList(text, "  - Warnings", segment.warnings);
                    AppendNamedTextList(text, "  - Exceptions", segment.exceptions);
                    text.AppendLine();
                }

                AppendNamedTextList(text, "- Line warnings", line.warnings);
                AppendNamedTextList(text, "- Line exceptions", line.exceptions);
                text.AppendLine();
            }

            return text.ToString();
        }

        private static void AppendNamedTextList(StringBuilder text, string title, List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            text.AppendLine(title);
            AppendTextList(text, values);
            text.AppendLine();
        }

        private static void AppendTextList(StringBuilder text, List<string> values)
        {
            if (values == null || values.Count == 0)
            {
                text.AppendLine("- none");
                return;
            }

            foreach (string value in values)
            {
                text.AppendLine($"- {value}");
            }
        }

        private static void AppendFieldList(StringBuilder text, List<MetroTrackGeometryField> fields, string indent)
        {
            if (fields == null || fields.Count == 0)
            {
                text.AppendLine($"{indent}- none");
                return;
            }

            foreach (MetroTrackGeometryField field in fields)
            {
                text.AppendLine($"{indent}- {field.fieldPath}: {field.valueSummary} ({field.typeName})");
            }
        }

        private static string JoinOrNone(List<string> values)
        {
            return values != null && values.Count > 0 ? string.Join(", ", values.ToArray()) : "none";
        }
    }

    [DataContract]
    public class MetroTrackGeometryDebugDocument
    {
        [DataMember(Order = 1)]
        public string version { get; set; }

        [DataMember(Order = 2)]
        public string exportedAtUtc { get; set; }

        [DataMember(Order = 3)]
        public string jsonPath { get; set; }

        [DataMember(Order = 4)]
        public string textPath { get; set; }

        [DataMember(Order = 5)]
        public int maxRouteSegmentSamplesPerLine { get; set; }

        [DataMember(Order = 6)]
        public MetroTrackGeometryEnvironment environment { get; set; } = new MetroTrackGeometryEnvironment();

        [DataMember(Order = 7)]
        public MetroTrackGeometrySummary summary { get; set; } = new MetroTrackGeometrySummary();

        [DataMember(Order = 8)]
        public List<MetroTrackGeometryLineDebug> lines { get; set; } = new List<MetroTrackGeometryLineDebug>();

        [DataMember(Order = 9)]
        public List<string> warnings { get; set; } = new List<string>();

        [DataMember(Order = 10)]
        public List<string> exceptions { get; set; } = new List<string>();
    }

    [DataContract]
    public class MetroTrackGeometryEnvironment
    {
        [DataMember(Order = 1)]
        public bool hasWorld { get; set; }

        [DataMember(Order = 2)]
        public bool worldIsCreated { get; set; }

        [DataMember(Order = 3)]
        public string worldName { get; set; }

        [DataMember(Order = 4)]
        public int totalEntityCount { get; set; }
    }

    [DataContract]
    public class MetroTrackGeometrySummary
    {
        [DataMember(Order = 1)]
        public int transportLineCount { get; set; }

        [DataMember(Order = 2)]
        public int subwayLineCount { get; set; }

        [DataMember(Order = 3)]
        public int totalRouteSegments { get; set; }

        [DataMember(Order = 4)]
        public int totalSampledSegments { get; set; }

        [DataMember(Order = 5)]
        public int totalReferencedGameNetEntities { get; set; }

        [DataMember(Order = 6)]
        public int totalGeometryLikeFields { get; set; }

        [DataMember(Order = 7)]
        public List<string> likelyCurveSourceCandidates { get; set; } = new List<string>();

        [DataMember(Order = 8)]
        public string recoverability { get; set; }
    }

    [DataContract]
    public class MetroTrackGeometryLineDebug
    {
        [DataMember(Order = 1)]
        public string lineEntityId { get; set; }

        [DataMember(Order = 2)]
        public string lineName { get; set; }

        [DataMember(Order = 3)]
        public int routeNumber { get; set; }

        [DataMember(Order = 4)]
        public int waypointCount { get; set; }

        [DataMember(Order = 5)]
        public string subwayMatchReason { get; set; }

        [DataMember(Order = 6)]
        public bool hasRouteSegmentBuffer { get; set; }

        [DataMember(Order = 7)]
        public int routeSegmentCount { get; set; }

        [DataMember(Order = 8)]
        public int sampledSegmentCount { get; set; }

        [DataMember(Order = 9)]
        public int referencedGameNetEntityCount { get; set; }

        [DataMember(Order = 10)]
        public int geometryLikeFieldCount { get; set; }

        [DataMember(Order = 11)]
        public List<string> likelyCurveSourceCandidates { get; set; } = new List<string>();

        [DataMember(Order = 12)]
        public List<MetroTrackGeometrySegmentDebug> segments { get; set; } = new List<MetroTrackGeometrySegmentDebug>();

        [DataMember(Order = 13)]
        public List<string> warnings { get; set; } = new List<string>();

        [DataMember(Order = 14)]
        public List<string> exceptions { get; set; } = new List<string>();
    }

    [DataContract]
    public class MetroTrackGeometrySegmentDebug
    {
        [DataMember(Order = 1)]
        public int segmentIndex { get; set; }

        [DataMember(Order = 2)]
        public string segmentEntityId { get; set; }

        [DataMember(Order = 3)]
        public List<string> componentTypes { get; set; } = new List<string>();

        [DataMember(Order = 4)]
        public List<MetroTrackGeometryEntityReference> entityReferenceFields { get; set; } = new List<MetroTrackGeometryEntityReference>();

        [DataMember(Order = 5)]
        public List<MetroTrackGeometryField> geometryLikeFields { get; set; } = new List<MetroTrackGeometryField>();

        [DataMember(Order = 6)]
        public List<MetroTrackGeometryReferencedEntity> referencedEntities { get; set; } = new List<MetroTrackGeometryReferencedEntity>();

        [DataMember(Order = 7)]
        public List<string> warnings { get; set; } = new List<string>();

        [DataMember(Order = 8)]
        public List<string> exceptions { get; set; } = new List<string>();
    }

    [DataContract]
    public class MetroTrackGeometryEntityReference
    {
        [DataMember(Order = 1)]
        public string fieldPath { get; set; }

        [DataMember(Order = 2)]
        public string entityId { get; set; }

        [IgnoreDataMember]
        public Entity Entity { get; set; }
    }

    [DataContract]
    public class MetroTrackGeometryReferencedEntity
    {
        [DataMember(Order = 1)]
        public string sourceFieldPath { get; set; }

        [DataMember(Order = 2)]
        public string entityId { get; set; }

        [DataMember(Order = 3)]
        public bool isGameNetEntity { get; set; }

        [DataMember(Order = 4)]
        public List<string> componentTypes { get; set; } = new List<string>();

        [DataMember(Order = 5)]
        public List<string> gameNetComponentTypes { get; set; } = new List<string>();

        [DataMember(Order = 6)]
        public List<MetroTrackGeometryField> geometryLikeFields { get; set; } = new List<MetroTrackGeometryField>();

        [DataMember(Order = 7)]
        public List<string> warnings { get; set; } = new List<string>();

        [DataMember(Order = 8)]
        public List<string> exceptions { get; set; } = new List<string>();
    }

    [DataContract]
    public class MetroTrackGeometryField
    {
        [DataMember(Order = 1)]
        public string fieldPath { get; set; }

        [DataMember(Order = 2)]
        public string typeName { get; set; }

        [DataMember(Order = 3)]
        public string valueSummary { get; set; }
    }
}
