using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.SceneFlow;
using Game.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using RouteColor = Game.Routes.Color;
using RouteTransportStop = Game.Routes.TransportStop;
using Transform = Game.Objects.Transform;

namespace CS2_Metro
{
    public static class RealMetroJsonExporter
    {
        private static readonly string[] Palette =
        {
            "#D71920",
            "#0072BC",
            "#00A651",
            "#F7941D",
            "#92278F",
            "#00AEEF",
            "#8DC63F",
            "#EC008C"
        };

        public static string GetDefaultExportDirectory()
        {
            return TestMetroJsonExporter.GetDefaultExportDirectory();
        }

        public static string GetJsonPath()
        {
            return Path.Combine(GetDefaultExportDirectory(), "metro-export.json");
        }

        public static string GetDiagnosticsPath()
        {
            return Path.Combine(GetDefaultExportDirectory(), "metro-export-diagnostics.txt");
        }

        public static bool ExportRealMetroJson(UpdateSystem updateSystem)
        {
            string jsonPath = GetJsonPath();
            string diagnosticsPath = GetDiagnosticsPath();
            Mod.log.Info($"Export Real Metro JSON started. JSON: {jsonPath}. Diagnostics: {diagnosticsPath}");

            try
            {
                Directory.CreateDirectory(GetDefaultExportDirectory());

                ExportContext context = new ExportContext(updateSystem, jsonPath, diagnosticsPath);
                MetroExport export = BuildExport(context);
                File.WriteAllText(jsonPath, BuildJson(export), new UTF8Encoding(false));
                File.WriteAllText(diagnosticsPath, context.Diagnostics.ToString(), new UTF8Encoding(false));

                Mod.log.Info($"Export Real Metro JSON succeeded. Lines: {export.Lines.Count}, stations: {export.Stations.Count}. JSON: {jsonPath}. Diagnostics: {diagnosticsPath}");
                return true;
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Export Real Metro JSON failed. JSON: {jsonPath}. Diagnostics: {diagnosticsPath}. Error: {ex}");

                try
                {
                    File.WriteAllText(diagnosticsPath, "Export Real Metro JSON failed." + Environment.NewLine + ex, new UTF8Encoding(false));
                }
                catch
                {
                }

                return false;
            }
        }

        private static MetroExport BuildExport(ExportContext context)
        {
            MetroExport export = new MetroExport
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            };

            context.Diagnostics.AppendLine("CS2 Metro Diagram - Real Metro Export Diagnostics");
            context.Diagnostics.AppendLine($"Exported UTC: {export.ExportedAtUtc}");
            context.Diagnostics.AppendLine($"JSON: {context.JsonPath}");
            context.Diagnostics.AppendLine($"Diagnostics: {context.DiagnosticsPath}");
            context.Diagnostics.AppendLine();

            if (context.UpdateSystem == null)
            {
                context.Diagnostics.AppendLine("UpdateSystem is null. Outputting empty network.");
                return export;
            }

            World world = null;
            try
            {
                world = context.UpdateSystem.World;
            }
            catch (Exception ex)
            {
                context.Diagnostics.AppendLine($"Failed to read world: {ex}");
            }

            if (world == null || !world.IsCreated)
            {
                context.Diagnostics.AppendLine("World is unavailable or not created. Outputting empty network.");
                return export;
            }

            context.EntityManager = context.UpdateSystem.EntityManager;
            context.NameSystem = TryGetNameSystem(world, context.Diagnostics);

            using (EntityQuery query = context.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TransportLine>()))
            using (NativeArray<Entity> lineEntities = query.ToEntityArray(Allocator.Temp))
            {
                context.Diagnostics.AppendLine($"TransportLine entities found: {lineEntities.Length}");
                context.Diagnostics.AppendLine();

                for (int i = 0; i < lineEntities.Length; i++)
                {
                    Entity lineEntity = lineEntities[i];
                    ProcessLine(context, export, lineEntity, i);
                }
            }

            context.Diagnostics.AppendLine();
            context.Diagnostics.AppendLine($"Subway lines exported: {export.Lines.Count}");
            context.Diagnostics.AppendLine($"Stations exported: {export.Stations.Count}");

            return export;
        }

        private static NameSystem TryGetNameSystem(World world, StringBuilder diagnostics)
        {
            try
            {
                return world.GetExistingSystemManaged<NameSystem>();
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"NameSystem unavailable; fallback names may be used. Error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void ProcessLine(ExportContext context, MetroExport export, Entity lineEntity, int lineIndex)
        {
            string lineEntityId = FormatEntityId("line", lineEntity);
            int routeNumber = ReadRouteNumber(context.EntityManager, lineEntity);
            int waypointCount = GetWaypointCount(context.EntityManager, lineEntity);
            string color = ReadLineColor(context.EntityManager, lineEntity, export.Lines.Count, context.Diagnostics, lineEntityId);
            string lineName = ReadDisplayName(context, lineEntity);
            bool usedFallbackLineName = false;

            if (string.IsNullOrWhiteSpace(lineName))
            {
                lineName = routeNumber > 0 ? $"Metro Line {routeNumber}" : $"Metro Line {lineIndex + 1}";
                usedFallbackLineName = true;
            }

            context.Diagnostics.AppendLine($"Line candidate {lineEntityId}");
            context.Diagnostics.AppendLine($"- RouteNumber: {(routeNumber > 0 ? routeNumber.ToString(CultureInfo.InvariantCulture) : "unavailable")}");
            context.Diagnostics.AppendLine($"- Color: {color}");
            context.Diagnostics.AppendLine($"- Waypoint count: {waypointCount}");
            context.Diagnostics.AppendLine($"- Name: {lineName}" + (usedFallbackLineName ? " (fallback)" : string.Empty));

            if (!IsSubwayLine(context, lineEntity, out string subwayReason))
            {
                context.Diagnostics.AppendLine($"- Skipped line: not identified as Subway. Checks: {subwayReason}");
                context.Diagnostics.AppendLine();
                return;
            }

            context.Diagnostics.AppendLine($"- Subway match: {subwayReason}");

            MetroLineExport line = new MetroLineExport
            {
                Id = lineEntityId,
                Name = lineName,
                Color = color,
                Mode = "metro"
            };

            if (!context.EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
            {
                context.Diagnostics.AppendLine("- Skipped line: no RouteWaypoint buffer.");
                context.Diagnostics.AppendLine();
                return;
            }

            DynamicBuffer<RouteWaypoint> waypoints = context.EntityManager.GetBuffer<RouteWaypoint>(lineEntity, true);
            for (int i = 0; i < waypoints.Length; i++)
            {
                ProcessWaypoint(context, export, line, lineEntityId, waypoints[i], i);
            }

            if (line.Stops.Count < 2)
            {
                context.Diagnostics.AppendLine($"- Warning: exported line has fewer than two stops ({line.Stops.Count}).");
            }

            export.Lines.Add(line);
            context.Diagnostics.AppendLine($"- Exported stops: {line.Stops.Count}");
            context.Diagnostics.AppendLine();
        }

        private static void ProcessWaypoint(ExportContext context, MetroExport export, MetroLineExport line, string lineEntityId, RouteWaypoint routeWaypoint, int waypointIndex)
        {
            Entity waypointEntity = routeWaypoint.m_Waypoint;

            if (!IsValidEntity(context.EntityManager, waypointEntity))
            {
                context.Diagnostics.AppendLine($"  - Skipped waypoint[{waypointIndex}]: waypoint entity is invalid.");
                return;
            }

            float3 waypointPosition = float3.zero;
            bool hasWaypointPosition = TryReadPosition(context.EntityManager, waypointEntity, out waypointPosition);

            Entity connectedStop = ReadConnectedEntity(context.EntityManager, waypointEntity);
            if (!IsValidEntity(context.EntityManager, connectedStop))
            {
                context.Diagnostics.AppendLine($"  - Skipped waypoint[{waypointIndex}] {FormatEntity(waypointEntity)}: no valid Connected.m_Connected.");
                return;
            }

            bool isSubwayStop = context.EntityManager.HasComponent<SubwayStop>(connectedStop);
            bool isTransportStop = context.EntityManager.HasComponent<RouteTransportStop>(connectedStop);
            if (!isSubwayStop && !isTransportStop)
            {
                context.Diagnostics.AppendLine($"  - Skipped waypoint[{waypointIndex}] {FormatEntity(waypointEntity)}: connected entity {FormatEntity(connectedStop)} has no SubwayStop or TransportStop.");
                return;
            }

            StationIdentity identity = ResolveStationIdentity(context.EntityManager, connectedStop, waypointEntity);
            StationPosition stationPosition = ResolveStationPosition(context.EntityManager, connectedStop, waypointEntity, identity.GroupEntity, waypointPosition, hasWaypointPosition);

            MetroStationExport station;
            if (!export.StationsById.TryGetValue(identity.StationId, out station))
            {
                bool usedFallbackName;
                string stationName = ReadStationName(context, identity.GroupEntity, connectedStop, waypointEntity, export.Stations.Count + 1, out usedFallbackName);

                station = new MetroStationExport
                {
                    Id = identity.StationId,
                    Name = stationName,
                    X = stationPosition.Position.x,
                    Z = stationPosition.Position.z,
                    IsInterchange = false
                };
                export.StationsById.Add(station.Id, station);
                export.Stations.Add(station);

                context.Diagnostics.AppendLine($"  - Station {station.Id}: source={identity.Source}, group={FormatEntity(identity.GroupEntity)}, name='{station.Name}'" + (usedFallbackName ? " (fallback name)" : string.Empty) + $", positionSource={stationPosition.Source}" + (stationPosition.UsedFallback ? " (fallback coordinate)" : string.Empty));
            }

            if (!station.Lines.Contains(line.Id))
            {
                station.Lines.Add(line.Id);
            }

            station.IsInterchange = station.Lines.Count > 1;

            line.Stops.Add(station.Id);
        }

        private static bool IsSubwayLine(ExportContext context, Entity lineEntity, out string reason)
        {
            List<string> checks = new List<string>();

            try
            {
                if (context.EntityManager.HasComponent<PrefabRef>(lineEntity))
                {
                    Entity prefab = context.EntityManager.GetComponentData<PrefabRef>(lineEntity).m_Prefab;
                    if (IsTransportLinePrefabSubway(context.EntityManager, prefab))
                    {
                        reason = $"TransportLineData prefab {FormatEntity(prefab)} transport type Subway";
                        return true;
                    }

                    checks.Add($"TransportLineData prefab {FormatEntity(prefab)} not Subway/unreadable");
                }
                else
                {
                    checks.Add("No PrefabRef on line");
                }
            }
            catch (Exception ex)
            {
                checks.Add($"PrefabRef check failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                if (context.EntityManager.HasBuffer<VehicleModel>(lineEntity))
                {
                    DynamicBuffer<VehicleModel> models = context.EntityManager.GetBuffer<VehicleModel>(lineEntity, true);
                    if (models.Length > 0)
                    {
                        Entity primaryPrefab = models[0].m_PrimaryPrefab;
                        if (IsVehiclePrefabSubway(context.EntityManager, primaryPrefab))
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
                else
                {
                    checks.Add("No VehicleModel buffer");
                }
            }
            catch (Exception ex)
            {
                checks.Add($"VehicleModel check failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                if (context.EntityManager.HasBuffer<RouteWaypoint>(lineEntity))
                {
                    DynamicBuffer<RouteWaypoint> waypoints = context.EntityManager.GetBuffer<RouteWaypoint>(lineEntity, true);
                    for (int i = 0; i < waypoints.Length; i++)
                    {
                        Entity waypoint = waypoints[i].m_Waypoint;
                        Entity connected = ReadConnectedEntity(context.EntityManager, waypoint);
                        if (IsValidEntity(context.EntityManager, connected) && context.EntityManager.HasComponent<SubwayStop>(connected))
                        {
                            reason = $"RouteWaypoint[{i}] connected to SubwayStop {FormatEntity(connected)}";
                            return true;
                        }
                    }

                    checks.Add("No waypoint connected to SubwayStop");
                }
                else
                {
                    checks.Add("No RouteWaypoint buffer");
                }
            }
            catch (Exception ex)
            {
                checks.Add($"Waypoint SubwayStop check failed: {ex.GetType().Name}: {ex.Message}");
            }

            reason = string.Join("; ", checks.ToArray());
            return false;
        }

        private static bool IsTransportLinePrefabSubway(EntityManager entityManager, Entity prefab)
        {
            return IsValidEntity(entityManager, prefab)
                && entityManager.HasComponent<TransportLineData>(prefab)
                && entityManager.GetComponentData<TransportLineData>(prefab).m_TransportType == TransportType.Subway;
        }

        private static bool IsVehiclePrefabSubway(EntityManager entityManager, Entity prefab)
        {
            return IsValidEntity(entityManager, prefab)
                && entityManager.HasComponent<PublicTransportVehicleData>(prefab)
                && entityManager.GetComponentData<PublicTransportVehicleData>(prefab).m_TransportType == TransportType.Subway;
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

        private static string ReadLineColor(EntityManager entityManager, Entity lineEntity, int subwayLineIndex, StringBuilder diagnostics, string lineId)
        {
            try
            {
                if (entityManager.HasComponent<RouteColor>(lineEntity))
                {
                    Color32 color = entityManager.GetComponentData<RouteColor>(lineEntity).m_Color;
                    return $"#{color.r:X2}{color.g:X2}{color.b:X2}";
                }
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"- Failed to read color for {lineId}: {ex.GetType().Name}: {ex.Message}");
            }

            string fallback = Palette[subwayLineIndex % Palette.Length];
            diagnostics.AppendLine($"- Used fallback color for {lineId}: {fallback}");
            return fallback;
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

        private static StationIdentity ResolveStationIdentity(EntityManager entityManager, Entity connectedStop, Entity waypointEntity)
        {
            try
            {
                if (entityManager.HasComponent<RouteTransportStop>(connectedStop))
                {
                    Entity accessRestriction = entityManager.GetComponentData<RouteTransportStop>(connectedStop).m_AccessRestriction;
                    if (IsValidEntity(entityManager, accessRestriction))
                    {
                        return new StationIdentity(accessRestriction, FormatEntityId("station", accessRestriction), "AccessRestriction");
                    }
                }
            }
            catch
            {
            }

            Entity owner = ReadOwner(entityManager, connectedStop);
            if (!IsValidEntity(entityManager, owner))
            {
                owner = ReadOwner(entityManager, waypointEntity);
            }

            if (IsValidEntity(entityManager, owner))
            {
                return new StationIdentity(owner, FormatEntityId("station", owner), "Owner");
            }

            Entity parent = ReadParent(entityManager, connectedStop);
            if (!IsValidEntity(entityManager, parent))
            {
                parent = ReadParent(entityManager, waypointEntity);
            }

            if (IsValidEntity(entityManager, parent))
            {
                return new StationIdentity(parent, FormatEntityId("station", parent), "Parent");
            }

            if (IsValidEntity(entityManager, connectedStop))
            {
                return new StationIdentity(connectedStop, FormatEntityId("station", connectedStop), "Stop");
            }

            return new StationIdentity(waypointEntity, FormatEntityId("station", waypointEntity), "Waypoint");
        }

        private static Entity ReadOwner(EntityManager entityManager, Entity entity)
        {
            try
            {
                return IsValidEntity(entityManager, entity) && entityManager.HasComponent<Owner>(entity)
                    ? entityManager.GetComponentData<Owner>(entity).m_Owner
                    : Entity.Null;
            }
            catch
            {
                return Entity.Null;
            }
        }

        private static Entity ReadParent(EntityManager entityManager, Entity entity)
        {
            try
            {
                return IsValidEntity(entityManager, entity) && entityManager.HasComponent<Attached>(entity)
                    ? entityManager.GetComponentData<Attached>(entity).m_Parent
                    : Entity.Null;
            }
            catch
            {
                return Entity.Null;
            }
        }

        private static StationPosition ResolveStationPosition(EntityManager entityManager, Entity connectedStop, Entity waypointEntity, Entity groupEntity, float3 waypointPosition, bool hasWaypointPosition)
        {
            float3 position;

            if (TryReadTransform(entityManager, groupEntity, out position))
            {
                return new StationPosition(position, "StationGroupTransform", false);
            }

            if (TryReadTransform(entityManager, connectedStop, out position))
            {
                return new StationPosition(position, "StopTransform", false);
            }

            if (TryReadPosition(entityManager, connectedStop, out position))
            {
                return new StationPosition(position, "StopPosition", true);
            }

            if (TryReadTransform(entityManager, waypointEntity, out position))
            {
                return new StationPosition(position, "WaypointTransform", true);
            }

            if (hasWaypointPosition)
            {
                return new StationPosition(waypointPosition, "WaypointPosition", true);
            }

            return new StationPosition(float3.zero, "ZeroFallback", true);
        }

        private static bool TryReadTransform(EntityManager entityManager, Entity entity, out float3 position)
        {
            position = float3.zero;
            try
            {
                if (IsValidEntity(entityManager, entity) && entityManager.HasComponent<Transform>(entity))
                {
                    position = entityManager.GetComponentData<Transform>(entity).m_Position;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryReadPosition(EntityManager entityManager, Entity entity, out float3 position)
        {
            position = float3.zero;
            try
            {
                if (IsValidEntity(entityManager, entity) && entityManager.HasComponent<Position>(entity))
                {
                    position = entityManager.GetComponentData<Position>(entity).m_Position;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string ReadStationName(ExportContext context, Entity groupEntity, Entity connectedStop, Entity waypointEntity, int stationNumber, out bool usedFallback)
        {
            string name = ReadDisplayName(context, groupEntity);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadDisplayName(context, connectedStop);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ReadDisplayName(context, waypointEntity);
            }

            usedFallback = string.IsNullOrWhiteSpace(name);
            return usedFallback ? $"Station {stationNumber}" : name;
        }

        private static string ReadDisplayName(ExportContext context, Entity entity)
        {
            if (!IsValidEntity(context.EntityManager, entity))
            {
                return string.Empty;
            }

            try
            {
                if (context.NameSystem != null)
                {
                    string renderedName = context.NameSystem.GetRenderedLabelName(entity);
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
                string entityName = context.EntityManager.GetName(entity);
                return string.IsNullOrWhiteSpace(entityName) ? string.Empty : entityName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsValidEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null && entity.Index >= 0 && entityManager.Exists(entity);
        }

        private static string BuildJson(MetroExport export)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"schemaVersion\": 1,");
            json.AppendLine("  \"generator\": {");
            json.AppendLine("    \"name\": \"CS2 Metro Diagram Real Exporter\",");
            json.AppendLine($"    \"version\": \"{VersionInfo.ReleaseVersion}\"");
            json.AppendLine("  },");
            json.AppendLine("  \"game\": {");
            json.AppendLine("    \"name\": \"Cities: Skylines II\",");
            json.AppendLine("    \"version\": \"unknown\"");
            json.AppendLine("  },");
            json.AppendLine("  \"city\": {");
            json.AppendLine("    \"name\": \"CS2 Metro Export\",");
            json.AppendLine($"    \"exportedAtUtc\": \"{EscapeJson(export.ExportedAtUtc)}\"");
            json.AppendLine("  },");
            json.AppendLine("  \"network\": {");
            json.AppendLine("    \"type\": \"metro\",");
            json.AppendLine("    \"stations\": [");

            for (int i = 0; i < export.Stations.Count; i++)
            {
                MetroStationExport station = export.Stations[i];
                json.AppendLine("      {");
                json.AppendLine($"        \"id\": \"{EscapeJson(station.Id)}\",");
                json.AppendLine($"        \"name\": \"{EscapeJson(station.Name)}\",");
                json.AppendLine($"        \"position\": {{ \"x\": {FormatDouble(station.X)}, \"z\": {FormatDouble(station.Z)} }},");
                json.AppendLine($"        \"lines\": [{string.Join(", ", station.Lines.Select(lineId => $"\"{EscapeJson(lineId)}\"").ToArray())}],");
                json.AppendLine($"        \"isInterchange\": {(station.IsInterchange ? "true" : "false")}");
                json.Append("      }");
                json.AppendLine(i == export.Stations.Count - 1 ? string.Empty : ",");
            }

            json.AppendLine("    ],");
            json.AppendLine("    \"lines\": [");

            for (int i = 0; i < export.Lines.Count; i++)
            {
                MetroLineExport line = export.Lines[i];
                json.AppendLine("      {");
                json.AppendLine($"        \"id\": \"{EscapeJson(line.Id)}\",");
                json.AppendLine($"        \"name\": \"{EscapeJson(line.Name)}\",");
                json.AppendLine($"        \"color\": \"{EscapeJson(line.Color)}\",");
                json.AppendLine($"        \"mode\": \"{EscapeJson(line.Mode)}\",");
                json.AppendLine($"        \"stops\": [{string.Join(", ", line.Stops.Select(stopId => $"\"{EscapeJson(stopId)}\"").ToArray())}]");
                json.Append("      }");
                json.AppendLine(i == export.Lines.Count - 1 ? string.Empty : ",");
            }

            json.AppendLine("    ]");
            json.AppendLine("  }");
            json.AppendLine("}");
            return json.ToString();
        }

        private static string FormatEntityId(string prefix, Entity entity)
        {
            return $"{prefix}_{entity.Index}_{entity.Version}";
        }

        private static string FormatEntity(Entity entity)
        {
            return $"{entity.Index}:{entity.Version}";
        }

        private static string FormatDouble(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string EscapeJson(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            StringBuilder escaped = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            escaped.Append("\\u");
                            escaped.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(c);
                        }
                        break;
                }
            }

            return escaped.ToString();
        }

        private sealed class ExportContext
        {
            public ExportContext(UpdateSystem updateSystem, string jsonPath, string diagnosticsPath)
            {
                UpdateSystem = updateSystem;
                JsonPath = jsonPath;
                DiagnosticsPath = diagnosticsPath;
            }

            public UpdateSystem UpdateSystem { get; }

            public string JsonPath { get; }

            public string DiagnosticsPath { get; }

            public EntityManager EntityManager { get; set; }

            public NameSystem NameSystem { get; set; }

            public StringBuilder Diagnostics { get; } = new StringBuilder();
        }

        private sealed class MetroExport
        {
            public string ExportedAtUtc { get; set; }

            public List<MetroStationExport> Stations { get; } = new List<MetroStationExport>();

            public Dictionary<string, MetroStationExport> StationsById { get; } = new Dictionary<string, MetroStationExport>();

            public List<MetroLineExport> Lines { get; } = new List<MetroLineExport>();
        }

        private sealed class MetroStationExport
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public float X { get; set; }

            public float Z { get; set; }

            public List<string> Lines { get; } = new List<string>();

            public bool IsInterchange { get; set; }
        }

        private sealed class MetroLineExport
        {
            public string Id { get; set; }

            public string Name { get; set; }

            public string Color { get; set; }

            public string Mode { get; set; }

            public List<string> Stops { get; } = new List<string>();
        }

        private struct StationIdentity
        {
            public StationIdentity(Entity groupEntity, string stationId, string source)
            {
                GroupEntity = groupEntity;
                StationId = stationId;
                Source = source;
            }

            public Entity GroupEntity { get; }

            public string StationId { get; }

            public string Source { get; }
        }

        private struct StationPosition
        {
            public StationPosition(float3 position, string source, bool usedFallback)
            {
                Position = position;
                Source = source;
                UsedFallback = usedFallback;
            }

            public float3 Position { get; }

            public string Source { get; }

            public bool UsedFallback { get; }
        }
    }
}
