using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Entities;

namespace CS2_Metro
{
    internal static class RouteGeometryDiagnostics
    {
        private const int MaxRouteSegmentSamplesPerLine = 10;
        private const int MaxReferencedEntitiesPerSegment = 8;
        private const int MaxGeometryFieldsPerObject = 16;
        private const int MaxComponentFieldReadsPerReferencedEntity = 8;

        private static readonly string[] GeometryFieldKeywords =
        {
            "start",
            "end",
            "owner",
            "lane",
            "track",
            "curve",
            "path",
            "segment",
            "edge",
            "node",
            "position",
            "bezier"
        };

        private static readonly string[] NetComponentKeywords =
        {
            "track",
            "lane",
            "edge",
            "curve"
        };

        public static void AppendLineDiagnostics(EntityManager entityManager, NameSystem nameSystem, Entity lineEntity, string lineName, int routeNumber, int waypointCount, StringBuilder diagnostics, RouteGeometryDiagnosticsSummary summary)
        {
            if (diagnostics == null)
            {
                return;
            }

            summary = summary ?? new RouteGeometryDiagnosticsSummary();
            summary.TotalSubwayLines++;

            diagnostics.AppendLine("- Route Geometry Diagnostics:");
            diagnostics.AppendLine($"  - Line entity id: {FormatEntity(lineEntity)}");
            diagnostics.AppendLine($"  - Line name: {lineName}");
            diagnostics.AppendLine($"  - Route number: {(routeNumber > 0 ? routeNumber.ToString(CultureInfo.InvariantCulture) : "unavailable")}");
            diagnostics.AppendLine($"  - Waypoint count: {waypointCount}");

            try
            {
                ComponentType routeSegmentBufferType;
                Type routeSegmentElementType;
                if (!TryFindRouteSegmentBufferType(entityManager, lineEntity, out routeSegmentBufferType, out routeSegmentElementType))
                {
                    summary.LinesWithoutRouteSegmentBuffer++;
                    diagnostics.AppendLine("  - RouteSegment buffer exists: false");
                    diagnostics.AppendLine("  - Route segment count: 0");
                    return;
                }

                summary.LinesWithRouteSegmentBuffer++;
                object buffer = ReadBuffer(entityManager, lineEntity, routeSegmentElementType);
                int segmentCount = ReadBufferLength(buffer);
                int sampleCount = Math.Min(segmentCount, MaxRouteSegmentSamplesPerLine);
                summary.TotalRouteSegmentsFound += segmentCount;

                diagnostics.AppendLine("  - RouteSegment buffer exists: true");
                diagnostics.AppendLine($"  - Route segment count: {segmentCount}");
                diagnostics.AppendLine($"  - Sampled RouteSegments: {sampleCount} of {segmentCount}");

                PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
                if (itemProperty == null)
                {
                    diagnostics.AppendLine("  - RouteSegment buffer item reader unavailable.");
                    return;
                }

                for (int i = 0; i < sampleCount; i++)
                {
                    AppendSegmentDiagnostics(entityManager, buffer, itemProperty, routeSegmentElementType, i, diagnostics, summary);
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"  - Route geometry diagnostics failed for line {FormatEntity(lineEntity)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void AppendSummary(StringBuilder diagnostics, RouteGeometryDiagnosticsSummary summary)
        {
            if (diagnostics == null || summary == null)
            {
                return;
            }

            diagnostics.AppendLine();
            diagnostics.AppendLine("Route Geometry Diagnostics Summary");
            diagnostics.AppendLine($"- Total subway lines: {summary.TotalSubwayLines}");
            diagnostics.AppendLine($"- Lines with RouteSegment buffer: {summary.LinesWithRouteSegmentBuffer}");
            diagnostics.AppendLine($"- Lines without RouteSegment buffer: {summary.LinesWithoutRouteSegmentBuffer}");
            diagnostics.AppendLine($"- Total route segments found: {summary.TotalRouteSegmentsFound}");
            diagnostics.AppendLine($"- Sampled route segments: {summary.SampledRouteSegments}");
            diagnostics.AppendLine($"- Sampled segments exposing geometry-like fields: {summary.SampledSegmentsWithGeometryLikeFields}");
            diagnostics.AppendLine($"- Sampled segments referencing Game.Net entities: {summary.SampledSegmentsReferencingGameNetEntities}");

            string recoverability = summary.TotalRouteSegmentsFound > 0
                && (summary.SampledSegmentsWithGeometryLikeFields > 0 || summary.SampledSegmentsReferencingGameNetEntities > 0)
                    ? "likely recoverable from RouteSegment references"
                    : "not yet proven by this sample";
            diagnostics.AppendLine($"- Route geometry recoverability: {recoverability}");
        }

        public static string BuildSubwayTransportLineReport(EntityManager entityManager, NameSystem nameSystem)
        {
            StringBuilder report = new StringBuilder();
            RouteGeometryDiagnosticsSummary summary = new RouteGeometryDiagnosticsSummary();

            report.AppendLine("Route Geometry Diagnostics");
            report.AppendLine("This section is diagnostic-only and does not affect metro-export.json.");
            report.AppendLine();

            try
            {
                using (EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TransportLine>()))
                using (NativeArray<Entity> lineEntities = query.ToEntityArray(Allocator.Temp))
                {
                    report.AppendLine($"TransportLine entities found: {lineEntities.Length}");
                    report.AppendLine();

                    for (int i = 0; i < lineEntities.Length; i++)
                    {
                        Entity lineEntity = lineEntities[i];
                        string subwayReason;
                        if (!IsSubwayLine(entityManager, lineEntity, out subwayReason))
                        {
                            continue;
                        }

                        int routeNumber = ReadRouteNumber(entityManager, lineEntity);
                        int waypointCount = GetWaypointCount(entityManager, lineEntity);
                        string lineName = ReadDisplayName(entityManager, nameSystem, lineEntity);
                        if (string.IsNullOrWhiteSpace(lineName))
                        {
                            lineName = routeNumber > 0 ? $"Metro Line {routeNumber}" : $"Metro Line {i + 1}";
                        }

                        report.AppendLine($"Line candidate {FormatEntity(lineEntity)}");
                        report.AppendLine($"- Subway match: {subwayReason}");
                        AppendLineDiagnostics(entityManager, nameSystem, lineEntity, lineName, routeNumber, waypointCount, report, summary);
                        report.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"Route geometry line scan failed: {ex.GetType().Name}: {ex.Message}");
            }

            AppendSummary(report, summary);
            return report.ToString();
        }

        private static void AppendSegmentDiagnostics(EntityManager entityManager, object buffer, PropertyInfo itemProperty, Type routeSegmentElementType, int segmentIndex, StringBuilder diagnostics, RouteGeometryDiagnosticsSummary summary)
        {
            summary.SampledRouteSegments++;

            diagnostics.AppendLine($"    Segment sample[{segmentIndex}]");

            try
            {
                object segmentElement = itemProperty.GetValue(buffer, new object[] { segmentIndex });
                List<FieldReadResult> segmentFields = ReadGeometryLikeFields(segmentElement, routeSegmentElementType, MaxGeometryFieldsPerObject);
                List<EntityReferenceInfo> entityReferences = FindEntityReferences(segmentElement, routeSegmentElementType);
                Entity segmentEntity = ChooseSegmentEntity(entityManager, entityReferences);
                bool hasGeometryLikeFields = segmentFields.Count > 0;
                bool referencesGameNetEntity = false;

                diagnostics.AppendLine($"    - Segment entity id: {(IsValidEntity(entityManager, segmentEntity) ? FormatEntity(segmentEntity) : "unavailable")}");
                diagnostics.AppendLine($"    - Readable entity references: {(entityReferences.Count > 0 ? "true" : "false")}");

                if (segmentFields.Count == 0)
                {
                    diagnostics.AppendLine("    - Segment fields that look like geometry: none");
                }
                else
                {
                    diagnostics.AppendLine("    - Segment fields that look like geometry:");
                    foreach (FieldReadResult field in segmentFields)
                    {
                        diagnostics.AppendLine($"      - {field.Name}: {field.Value}");
                    }
                }

                if (IsValidEntity(entityManager, segmentEntity))
                {
                    List<ComponentType> segmentComponentTypes = GetComponentTypes(entityManager, segmentEntity);
                    List<string> segmentComponentTypeNames = segmentComponentTypes.Select(GetComponentTypeName).OrderBy(name => name).ToList();
                    diagnostics.AppendLine($"    - Component types on segment entity: {JoinOrNone(segmentComponentTypeNames)}");
                }

                if (entityReferences.Count == 0)
                {
                    diagnostics.AppendLine("    - Referenced entities: none");
                }
                else
                {
                    diagnostics.AppendLine("    - Referenced entities:");
                    int referenceCount = 0;
                    foreach (EntityReferenceInfo reference in entityReferences)
                    {
                        if (referenceCount >= MaxReferencedEntitiesPerSegment)
                        {
                            diagnostics.AppendLine($"      - Additional references omitted: {entityReferences.Count - referenceCount}");
                            break;
                        }

                        referenceCount++;
                        AppendReferencedEntityDiagnostics(entityManager, reference, diagnostics, ref hasGeometryLikeFields, ref referencesGameNetEntity);
                    }
                }

                if (hasGeometryLikeFields)
                {
                    summary.SampledSegmentsWithGeometryLikeFields++;
                }

                if (referencesGameNetEntity)
                {
                    summary.SampledSegmentsReferencingGameNetEntities++;
                }

                diagnostics.AppendLine($"    - Has geometry-like fields: {hasGeometryLikeFields}");
                diagnostics.AppendLine($"    - References Game.Net entity: {referencesGameNetEntity}");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"    - Segment diagnostics failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void AppendReferencedEntityDiagnostics(EntityManager entityManager, EntityReferenceInfo reference, StringBuilder diagnostics, ref bool hasGeometryLikeFields, ref bool referencesGameNetEntity)
        {
            Entity referencedEntity = reference.Entity;
            diagnostics.AppendLine($"      - {reference.Path}: {FormatEntity(referencedEntity)}");

            if (!IsValidEntity(entityManager, referencedEntity))
            {
                diagnostics.AppendLine("        Exists: false");
                return;
            }

            try
            {
                List<ComponentType> componentTypes = GetComponentTypes(entityManager, referencedEntity);
                List<string> componentTypeNames = componentTypes.Select(GetComponentTypeName).OrderBy(name => name).ToList();
                List<string> gameNetComponents = componentTypeNames.Where(IsGameNetComponentType).ToList();
                List<string> gameNetGeometryComponents = gameNetComponents.Where(ContainsNetGeometryKeyword).ToList();

                if (gameNetComponents.Count > 0)
                {
                    referencesGameNetEntity = true;
                }

                diagnostics.AppendLine($"        Component types: {JoinOrNone(componentTypeNames)}");
                diagnostics.AppendLine($"        Game.Net components: {JoinOrNone(gameNetComponents)}");
                diagnostics.AppendLine($"        Game.Net track/lane/edge/curve components: {JoinOrNone(gameNetGeometryComponents)}");

                int componentsRead = 0;
                foreach (ComponentType componentType in componentTypes)
                {
                    if (componentsRead >= MaxComponentFieldReadsPerReferencedEntity)
                    {
                        break;
                    }

                    Type managedType = SafeGetManagedType(componentType);
                    if (managedType == null || componentType.IsZeroSized || componentType.IsBuffer)
                    {
                        continue;
                    }

                    if (!IsGameNetComponentType(GetComponentTypeName(componentType)) && !HasGeometryLikeMembers(managedType))
                    {
                        continue;
                    }

                    componentsRead++;
                    try
                    {
                        object value = ReadComponentValue(entityManager, referencedEntity, componentType, managedType);
                        List<FieldReadResult> fields = ReadGeometryLikeFields(value, managedType, MaxGeometryFieldsPerObject);
                        if (fields.Count == 0)
                        {
                            continue;
                        }

                        hasGeometryLikeFields = true;
                        diagnostics.AppendLine($"        Geometry-like fields on {managedType.FullName}:");
                        foreach (FieldReadResult field in fields)
                        {
                            diagnostics.AppendLine($"          - {field.Name}: {field.Value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnostics.AppendLine($"        Failed to read geometry-like fields from {GetComponentTypeName(componentType)}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"        Referenced entity diagnostics failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool TryFindRouteSegmentBufferType(EntityManager entityManager, Entity lineEntity, out ComponentType routeSegmentBufferType, out Type routeSegmentElementType)
        {
            routeSegmentBufferType = default(ComponentType);
            routeSegmentElementType = null;

            try
            {
                List<ComponentType> componentTypes = GetComponentTypes(entityManager, lineEntity);
                foreach (ComponentType componentType in componentTypes)
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
            }
            catch
            {
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
            if (lengthProperty == null)
            {
                return 0;
            }

            object length = lengthProperty.GetValue(buffer, null);
            return length == null ? 0 : Convert.ToInt32(length, CultureInfo.InvariantCulture);
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

        private static List<FieldReadResult> ReadGeometryLikeFields(object value, Type valueType, int maxFields)
        {
            List<FieldReadResult> fields = new List<FieldReadResult>();
            if (value == null || valueType == null)
            {
                return fields;
            }

            foreach (FieldInfo field in valueType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!IsGeometryLikeMember(field.Name))
                {
                    continue;
                }

                fields.Add(ReadField(value, field));
                if (fields.Count >= maxFields)
                {
                    return fields;
                }
            }

            foreach (PropertyInfo property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 || !IsGeometryLikeMember(property.Name))
                {
                    continue;
                }

                fields.Add(ReadProperty(value, property));
                if (fields.Count >= maxFields)
                {
                    return fields;
                }
            }

            return fields;
        }

        private static FieldReadResult ReadField(object value, FieldInfo field)
        {
            try
            {
                return new FieldReadResult(field.Name, SafeFormatValue(field.GetValue(value)));
            }
            catch (Exception ex)
            {
                return new FieldReadResult(field.Name, $"Failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static FieldReadResult ReadProperty(object value, PropertyInfo property)
        {
            try
            {
                return new FieldReadResult(property.Name, SafeFormatValue(property.GetValue(value, null)));
            }
            catch (Exception ex)
            {
                return new FieldReadResult(property.Name, $"Failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static List<EntityReferenceInfo> FindEntityReferences(object value, Type valueType)
        {
            List<EntityReferenceInfo> references = new List<EntityReferenceInfo>();
            CollectEntityReferences(value, valueType, string.Empty, references, 0);

            Dictionary<string, EntityReferenceInfo> distinct = new Dictionary<string, EntityReferenceInfo>();
            foreach (EntityReferenceInfo reference in references)
            {
                string key = $"{reference.Path}|{reference.Entity.Index}|{reference.Entity.Version}";
                if (!distinct.ContainsKey(key))
                {
                    distinct.Add(key, reference);
                }
            }

            return distinct.Values.ToList();
        }

        private static void CollectEntityReferences(object value, Type valueType, string path, List<EntityReferenceInfo> references, int depth)
        {
            if (value == null || valueType == null || depth > 2)
            {
                return;
            }

            if (valueType == typeof(Entity))
            {
                references.Add(new EntityReferenceInfo(path, (Entity)value));
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

                string childPath = string.IsNullOrWhiteSpace(path) ? field.Name : $"{path}.{field.Name}";
                if (field.FieldType == typeof(Entity))
                {
                    references.Add(new EntityReferenceInfo(childPath, (Entity)fieldValue));
                }
                else if (depth < 2 && ShouldInspectNestedType(field.FieldType))
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

                string childPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                if (property.PropertyType == typeof(Entity))
                {
                    references.Add(new EntityReferenceInfo(childPath, (Entity)propertyValue));
                }
                else if (depth < 2 && ShouldInspectNestedType(property.PropertyType))
                {
                    CollectEntityReferences(propertyValue, property.PropertyType, childPath, references, depth + 1);
                }
            }
        }

        private static bool ShouldInspectNestedType(Type type)
        {
            if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return false;
            }

            return type.IsValueType || (type.Namespace != null && (type.Namespace.StartsWith("Game.", StringComparison.Ordinal) || type.Namespace.StartsWith("Unity.", StringComparison.Ordinal)));
        }

        private static Entity ChooseSegmentEntity(EntityManager entityManager, List<EntityReferenceInfo> references)
        {
            Entity fallback = Entity.Null;
            foreach (EntityReferenceInfo reference in references)
            {
                if (!IsValidEntity(entityManager, reference.Entity))
                {
                    continue;
                }

                if (reference.Path.IndexOf("segment", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    else
                    {
                        checks.Add("VehicleModel buffer is empty");
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

        private static string ReadDisplayName(EntityManager entityManager, NameSystem nameSystem, Entity entity)
        {
            if (!IsValidEntity(entityManager, entity))
            {
                return string.Empty;
            }

            try
            {
                if (nameSystem != null)
                {
                    string renderedName = nameSystem.GetRenderedLabelName(entity);
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
                string entityName = entityManager.GetName(entity);
                return string.IsNullOrWhiteSpace(entityName) ? string.Empty : entityName;
            }
            catch
            {
                return string.Empty;
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

        private static bool IsValidEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null && entity.Index >= 0 && entityManager.Exists(entity);
        }

        private static bool HasGeometryLikeMembers(Type type)
        {
            return type != null
                && (type.GetFields(BindingFlags.Instance | BindingFlags.Public).Any(field => IsGeometryLikeMember(field.Name))
                    || type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Any(property => IsGeometryLikeMember(property.Name)));
        }

        private static bool IsGeometryLikeMember(string memberName)
        {
            return GeometryFieldKeywords.Any(keyword => memberName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsGameNetComponentType(string typeName)
        {
            return typeName != null && typeName.IndexOf("Game.Net.", StringComparison.Ordinal) >= 0;
        }

        private static bool ContainsNetGeometryKeyword(string typeName)
        {
            return typeName != null
                && NetComponentKeywords.Any(keyword => typeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string JoinOrNone(List<string> values)
        {
            return values != null && values.Count > 0 ? string.Join(", ", values.ToArray()) : "none";
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

        private struct EntityReferenceInfo
        {
            public EntityReferenceInfo(string path, Entity entity)
            {
                Path = path;
                Entity = entity;
            }

            public string Path { get; }

            public Entity Entity { get; }
        }

        private struct FieldReadResult
        {
            public FieldReadResult(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }

            public string Value { get; }
        }
    }

    internal sealed class RouteGeometryDiagnosticsSummary
    {
        public int TotalSubwayLines { get; set; }

        public int LinesWithRouteSegmentBuffer { get; set; }

        public int LinesWithoutRouteSegmentBuffer { get; set; }

        public int TotalRouteSegmentsFound { get; set; }

        public int SampledRouteSegments { get; set; }

        public int SampledSegmentsWithGeometryLikeFields { get; set; }

        public int SampledSegmentsReferencingGameNetEntities { get; set; }
    }
}
