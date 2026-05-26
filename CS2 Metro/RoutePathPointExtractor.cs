using MetroDiagram.Core.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;

namespace CS2_Metro
{
    internal static class RoutePathPointExtractor
    {
        private const double DeduplicationEpsilon = 0.01;
        private const int BezierSampleIntervals = 7;
        private const int MaxCurveElementFailures = 20;
        private const int MaxCurveElementDumps = 10;
        private const int MaxMemberSummaries = 12;

        public static RoutePathPointExtractionResult Extract(EntityManager entityManager, Entity lineEntity)
        {
            RoutePathPointExtractionResult result = new RoutePathPointExtractionResult();
            List<RoutePathPointExport> rawPathPoints = new List<RoutePathPointExport>();

            try
            {
                Type routeSegmentElementType;
                if (!TryFindRouteSegmentBufferType(entityManager, lineEntity, out routeSegmentElementType))
                {
                    result.EmptyReason = "RouteSegment buffer not found.";
                    result.FinalizePathPoints(rawPathPoints);
                    return result;
                }

                object buffer = ReadBuffer(entityManager, lineEntity, routeSegmentElementType);
                int segmentCount = ReadBufferLength(buffer);
                result.RouteSegmentCount = segmentCount;

                PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
                if (itemProperty == null)
                {
                    result.EmptyReason = "RouteSegment buffer item reader unavailable.";
                    result.FinalizePathPoints(rawPathPoints);
                    return result;
                }

                for (int i = 0; i < segmentCount; i++)
                {
                    ExtractSegmentPathPoints(entityManager, buffer, itemProperty, routeSegmentElementType, i, rawPathPoints, result);
                }

                result.FinalizePathPoints(rawPathPoints);
                if (result.PathPoints.Count == 0 && string.IsNullOrWhiteSpace(result.EmptyReason))
                {
                    result.EmptyReason = result.SkippedSegmentCount > 0
                        ? "All RouteSegment geometry reads failed or produced duplicate-only points."
                        : "RouteSegment buffer was empty.";
                }
            }
            catch (Exception ex)
            {
                result.EmptyReason = $"Path point extraction failed: {ex.GetType().Name}: {ex.Message}";
                result.FinalizePathPoints(rawPathPoints);
            }

            return result;
        }

        private static void ExtractSegmentPathPoints(EntityManager entityManager, object buffer, PropertyInfo itemProperty, Type routeSegmentElementType, int segmentIndex, List<RoutePathPointExport> rawPathPoints, RoutePathPointExtractionResult result)
        {
            Entity segmentEntity = Entity.Null;

            try
            {
                object segmentElement = itemProperty.GetValue(buffer, new object[] { segmentIndex });
                List<EntityReferenceInfo> entityReferences = FindEntityReferences(segmentElement, routeSegmentElementType);
                segmentEntity = ChooseSegmentEntity(entityManager, entityReferences);

                if (!IsValidEntity(entityManager, segmentEntity))
                {
                    result.RecordSkippedSegment(segmentIndex, "no readable RouteSegment entity reference");
                    return;
                }

                List<RoutePathPointExport> segmentPoints;
                int curveElementCount;
                string curveError;
                if (TryReadCurveElementPathPoints(entityManager, segmentEntity, segmentIndex, result, out segmentPoints, out curveElementCount, out curveError))
                {
                    result.CurveElementCount += curveElementCount;
                    result.CurveSamplePointCount += segmentPoints.Count;
                    rawPathPoints.AddRange(segmentPoints);
                    return;
                }

                result.CurveElementCount += curveElementCount;
                result.RecordCurveElementFailure(segmentIndex, segmentEntity, curveError);

                int pathElementCount;
                string pathElementError;
                if (TryReadPathElementPathPoints(entityManager, segmentEntity, out segmentPoints, out pathElementCount, out pathElementError))
                {
                    result.PathElementCount += pathElementCount;
                    rawPathPoints.AddRange(segmentPoints);
                    return;
                }

                result.PathElementCount += pathElementCount;

                RoutePathTargetPoints targetPoints;
                string pathTargetsError;
                if (TryReadPathTargets(entityManager, segmentEntity, out targetPoints, out pathTargetsError))
                {
                    result.PathTargetsFallbackCount++;
                    rawPathPoints.Add(CreatePathPoint(targetPoints.StartX, targetPoints.StartZ, "RouteSegment.PathTargets", segmentEntity));
                    rawPathPoints.Add(CreatePathPoint(targetPoints.EndX, targetPoints.EndZ, "RouteSegment.PathTargets", segmentEntity));
                    return;
                }

                result.RecordSkippedSegment(segmentIndex, string.Format(
                    CultureInfo.InvariantCulture,
                    "CurveElement failed ({0}); PathElement failed ({1}); PathTargets failed ({2})",
                    curveError,
                    pathElementError,
                    pathTargetsError));
            }
            catch (Exception ex)
            {
                string segment = IsValidEntity(entityManager, segmentEntity) ? " " + FormatEntity(segmentEntity) : string.Empty;
                result.RecordSkippedSegment(segmentIndex, $"{segment}{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool TryReadCurveElementPathPoints(EntityManager entityManager, Entity segmentEntity, int segmentIndex, RoutePathPointExtractionResult result, out List<RoutePathPointExport> pathPoints, out int curveElementCount, out string error)
        {
            pathPoints = new List<RoutePathPointExport>();
            curveElementCount = 0;
            error = string.Empty;

            try
            {
                ComponentType componentType;
                Type managedType;
                if (!TryFindComponentTypeByName(entityManager, segmentEntity, "CurveElement", true, out componentType, out managedType))
                {
                    error = $"segment {FormatEntity(segmentEntity)} has no CurveElement buffer";
                    return false;
                }

                object buffer = ReadBuffer(entityManager, segmentEntity, managedType);
                curveElementCount = ReadBufferLength(buffer);
                PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
                if (itemProperty == null)
                {
                    error = "CurveElement buffer item reader unavailable";
                    return false;
                }

                List<string> failures = new List<string>();
                for (int i = 0; i < curveElementCount; i++)
                {
                    object curveElement = itemProperty.GetValue(buffer, new object[] { i });
                    object curveValue;
                    Type curveType;
                    string curveMemberName;
                    if (!TryReadBezierCurveMember(curveElement, managedType, out curveValue, out curveType, out curveMemberName))
                    {
                        result.RecordCurveElementDump(segmentIndex, i, segmentEntity, DumpCurveElement(curveElement, managedType, null, null, "No Bezier-like m_Curve member was found."));
                        failures.Add($"CurveElement[{i}] did not expose m_Curve/Bezier member. Members: {DumpMemberNames(managedType)}");
                        continue;
                    }

                    result.RecordCurveElementDump(segmentIndex, i, segmentEntity, DumpCurveElement(curveElement, managedType, curveValue, curveType, $"Bezier member: {curveMemberName}"));

                    List<PathGeometryPoint> samples;
                    if (!TryExtractBezierSamples(curveValue, curveType, out samples))
                    {
                        failures.Add($"CurveElement[{i}] {curveMemberName} ({curveType.FullName}) could not be sampled. Members: {DumpMemberNames(curveType)}");
                        continue;
                    }

                    foreach (PathGeometryPoint point in samples)
                    {
                        pathPoints.Add(CreatePathPoint(point.X, point.Z, "RouteSegment.CurveElement", segmentEntity));
                    }
                }

                if (pathPoints.Count >= 2)
                {
                    return true;
                }

                error = curveElementCount == 0
                    ? "CurveElement buffer is empty"
                    : $"CurveElement buffer produced fewer than two sampled points. {JoinOrNone(failures)}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"failed to read CurveElement on segment {FormatEntity(segmentEntity)}: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static bool TryReadPathElementPathPoints(EntityManager entityManager, Entity segmentEntity, out List<RoutePathPointExport> pathPoints, out int pathElementCount, out string error)
        {
            return TryReadGeometryPathPoints(
                entityManager,
                segmentEntity,
                "PathElement",
                "RouteSegment.PathElement",
                preferBezier: false,
                out pathPoints,
                out pathElementCount,
                out error);
        }

        private static bool TryReadBezierCurveMember(object curveElement, Type curveElementType, out object curveValue, out Type curveType, out string memberName)
        {
            curveValue = null;
            curveType = null;
            memberName = string.Empty;

            string[] preferredNames = { "m_Curve", "Curve", "curve", "m_Bezier", "Bezier", "bezier" };
            foreach (string preferredName in preferredNames)
            {
                if (TryReadMember(curveElement, curveElementType, preferredName, out curveValue, out curveType) && IsBezierLikeType(curveType))
                {
                    memberName = preferredName;
                    return true;
                }
            }

            foreach (FieldInfo field in GetInstanceFields(curveElementType))
            {
                object value;
                try
                {
                    value = field.GetValue(curveElement);
                }
                catch
                {
                    continue;
                }

                if (value != null && (IsBezierLikeType(field.FieldType) || IsGeometryMemberName(field.Name)))
                {
                    List<PathGeometryPoint> samples;
                    if (IsBezierLikeType(field.FieldType) || TryExtractBezierSamples(value, field.FieldType, out samples))
                    {
                        curveValue = value;
                        curveType = field.FieldType;
                        memberName = field.Name;
                        return true;
                    }
                }
            }

            foreach (PropertyInfo property in GetInstanceProperties(curveElementType))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object value;
                try
                {
                    value = property.GetValue(curveElement, null);
                }
                catch
                {
                    continue;
                }

                if (value != null && (IsBezierLikeType(property.PropertyType) || IsGeometryMemberName(property.Name)))
                {
                    List<PathGeometryPoint> samples;
                    if (IsBezierLikeType(property.PropertyType) || TryExtractBezierSamples(value, property.PropertyType, out samples))
                    {
                        curveValue = value;
                        curveType = property.PropertyType;
                        memberName = property.Name;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryReadGeometryPathPoints(EntityManager entityManager, Entity segmentEntity, string componentName, string source, bool preferBezier, out List<RoutePathPointExport> pathPoints, out int elementCount, out string error)
        {
            pathPoints = new List<RoutePathPointExport>();
            elementCount = 0;
            error = string.Empty;

            try
            {
                ComponentType componentType;
                Type managedType;
                if (!TryFindComponentTypeByName(entityManager, segmentEntity, componentName, true, out componentType, out managedType))
                {
                    if (!TryFindComponentTypeByName(entityManager, segmentEntity, componentName, false, out componentType, out managedType))
                    {
                        error = $"segment {FormatEntity(segmentEntity)} has no {componentName} buffer or component";
                        return false;
                    }

                    object componentValue = ReadComponentValue(entityManager, segmentEntity, componentType, managedType);
                    elementCount = 1;
                    List<string> summaries = new List<string>();
                    ExtractGeometryPointsFromValue(componentValue, managedType, source, segmentEntity, pathPoints, summaries, preferBezier, 0);
                    if (pathPoints.Count >= 2)
                    {
                        return true;
                    }

                    error = $"{componentName} component produced fewer than two points. Fields: {JoinOrNone(summaries)}";
                    return false;
                }

                object buffer = ReadBuffer(entityManager, segmentEntity, managedType);
                elementCount = ReadBufferLength(buffer);
                PropertyInfo itemProperty = buffer.GetType().GetProperty("Item");
                if (itemProperty == null)
                {
                    error = $"{componentName} buffer item reader unavailable";
                    return false;
                }

                List<string> fieldSummaries = new List<string>();
                for (int i = 0; i < elementCount; i++)
                {
                    object element = itemProperty.GetValue(buffer, new object[] { i });
                    ExtractGeometryPointsFromValue(element, managedType, source, segmentEntity, pathPoints, fieldSummaries, preferBezier, 0);
                }

                if (pathPoints.Count >= 2)
                {
                    return true;
                }

                error = elementCount == 0
                    ? $"{componentName} buffer is empty"
                    : $"{componentName} buffer produced fewer than two points. Fields: {JoinOrNone(fieldSummaries)}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"failed to read {componentName} on segment {FormatEntity(segmentEntity)}: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static void ExtractGeometryPointsFromValue(object value, Type valueType, string source, Entity segmentEntity, List<RoutePathPointExport> pathPoints, List<string> memberSummaries, bool preferBezier, int depth)
        {
            if (value == null || valueType == null || depth > 3)
            {
                return;
            }

            if (preferBezier)
            {
                List<PathGeometryPoint> bezierSamples;
                if (TryExtractBezierSamples(value, valueType, out bezierSamples))
                {
                    foreach (PathGeometryPoint point in bezierSamples)
                    {
                        pathPoints.Add(CreatePathPoint(point.X, point.Z, source, segmentEntity));
                    }

                    AddMemberSummary(memberSummaries, $"{valueType.Name}: sampled Bezier with {bezierSamples.Count} points");
                    return;
                }
            }

            if (IsPositionLikeType(valueType) && TryReadStrictXz(value, valueType, out double x, out double z))
            {
                pathPoints.Add(CreatePathPoint(x, z, source, segmentEntity));
                AddMemberSummary(memberSummaries, $"{valueType.Name}: x={FormatDouble(x)}, z={FormatDouble(z)}");
                return;
            }

            if (!ShouldInspectNestedType(valueType))
            {
                return;
            }

            foreach (FieldInfo field in GetInstanceFields(valueType))
            {
                object memberValue;
                try
                {
                    memberValue = field.GetValue(value);
                }
                catch (Exception ex)
                {
                    AddMemberSummary(memberSummaries, $"{field.Name}=failed:{ex.GetType().Name}");
                    continue;
                }

                ReadGeometryMember(field.Name, memberValue, field.FieldType, source, segmentEntity, pathPoints, memberSummaries, preferBezier, depth);
            }

            foreach (PropertyInfo property in GetInstanceProperties(valueType))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object memberValue;
                try
                {
                    memberValue = property.GetValue(value, null);
                }
                catch (Exception ex)
                {
                    AddMemberSummary(memberSummaries, $"{property.Name}=failed:{ex.GetType().Name}");
                    continue;
                }

                ReadGeometryMember(property.Name, memberValue, property.PropertyType, source, segmentEntity, pathPoints, memberSummaries, preferBezier, depth);
            }
        }

        private static void ReadGeometryMember(string memberName, object memberValue, Type memberType, string source, Entity segmentEntity, List<RoutePathPointExport> pathPoints, List<string> memberSummaries, bool preferBezier, int depth)
        {
            if (memberValue == null || memberType == null)
            {
                return;
            }

            bool geometryName = IsGeometryMemberName(memberName);
            if (preferBezier && (geometryName || IsBezierLikeType(memberType)))
            {
                List<PathGeometryPoint> bezierSamples;
                if (TryExtractBezierSamples(memberValue, memberType, out bezierSamples))
                {
                    foreach (PathGeometryPoint point in bezierSamples)
                    {
                        pathPoints.Add(CreatePathPoint(point.X, point.Z, source, segmentEntity));
                    }

                    AddMemberSummary(memberSummaries, $"{memberName}: sampled Bezier with {bezierSamples.Count} points");
                    return;
                }
            }

            if (geometryName && IsPositionLikeType(memberType) && TryReadStrictXz(memberValue, memberType, out double x, out double z))
            {
                pathPoints.Add(CreatePathPoint(x, z, source, segmentEntity));
                AddMemberSummary(memberSummaries, $"{memberName}=x:{FormatDouble(x)},z:{FormatDouble(z)}");
                return;
            }

            if (geometryName)
            {
                AddMemberSummary(memberSummaries, $"{memberName}={SafeFormatValue(memberValue)}");
            }

            if (depth < 3 && ShouldInspectNestedType(memberType))
            {
                ExtractGeometryPointsFromValue(memberValue, memberType, source, segmentEntity, pathPoints, memberSummaries, preferBezier, depth + 1);
            }
        }

        private static bool TryExtractBezierSamples(object value, Type valueType, out List<PathGeometryPoint> samples)
        {
            samples = new List<PathGeometryPoint>();

            PathGeometryPoint p0;
            PathGeometryPoint p1;
            PathGeometryPoint p2;
            PathGeometryPoint p3;
            if (!TryReadBezierControlPoints(value, valueType, out p0, out p1, out p2, out p3))
            {
                return false;
            }

            samples.AddRange(PathGeometrySampler.SampleCubicBezier(p0, p1, p2, p3, BezierSampleIntervals));
            return samples.Count >= 2;
        }

        private static bool TryReadBezierControlPoints(object value, Type valueType, out PathGeometryPoint p0, out PathGeometryPoint p1, out PathGeometryPoint p2, out PathGeometryPoint p3)
        {
            p0 = default(PathGeometryPoint);
            p1 = default(PathGeometryPoint);
            p2 = default(PathGeometryPoint);
            p3 = default(PathGeometryPoint);

            string[] aNames = { "a", "A", "m_A", "p0", "P0", "c0" };
            string[] bNames = { "b", "B", "m_B", "p1", "P1", "c1" };
            string[] cNames = { "c", "C", "m_C", "p2", "P2", "c2" };
            string[] dNames = { "d", "D", "m_D", "p3", "P3", "c3" };

            if (TryReadPointMember(value, valueType, aNames, out p0)
                && TryReadPointMember(value, valueType, bNames, out p1)
                && TryReadPointMember(value, valueType, cNames, out p2)
                && TryReadPointMember(value, valueType, dNames, out p3))
            {
                return true;
            }

            return TryReadFloat4x3ControlPoints(value, valueType, out p0, out p1, out p2, out p3);
        }

        private static bool TryReadPointMember(object value, Type valueType, string[] memberNames, out PathGeometryPoint point)
        {
            point = default(PathGeometryPoint);
            foreach (string memberName in memberNames)
            {
                object memberValue;
                Type memberType;
                if (!TryReadMember(value, valueType, memberName, out memberValue, out memberType))
                {
                    continue;
                }

                if (TryReadStrictXz(memberValue, memberType, out double x, out double z))
                {
                    point = new PathGeometryPoint(x, z);
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadFloat4x3ControlPoints(object value, Type valueType, out PathGeometryPoint p0, out PathGeometryPoint p1, out PathGeometryPoint p2, out PathGeometryPoint p3)
        {
            p0 = default(PathGeometryPoint);
            p1 = default(PathGeometryPoint);
            p2 = default(PathGeometryPoint);
            p3 = default(PathGeometryPoint);

            object xColumn;
            Type xColumnType;
            object zColumn;
            Type zColumnType;
            if (!TryReadMember(value, valueType, "c0", out xColumn, out xColumnType)
                || !TryReadMember(value, valueType, "c2", out zColumn, out zColumnType))
            {
                return false;
            }

            double[] xs;
            double[] zs;
            if (!TryReadFloat4Values(xColumn, xColumnType, out xs)
                || !TryReadFloat4Values(zColumn, zColumnType, out zs))
            {
                return false;
            }

            p0 = new PathGeometryPoint(xs[0], zs[0]);
            p1 = new PathGeometryPoint(xs[1], zs[1]);
            p2 = new PathGeometryPoint(xs[2], zs[2]);
            p3 = new PathGeometryPoint(xs[3], zs[3]);
            return true;
        }

        private static bool TryReadFloat4Values(object value, Type valueType, out double[] values)
        {
            values = new double[4];
            return TryReadNumericMember(value, valueType, "x", out values[0])
                && TryReadNumericMember(value, valueType, "y", out values[1])
                && TryReadNumericMember(value, valueType, "z", out values[2])
                && TryReadNumericMember(value, valueType, "w", out values[3]);
        }

        private static bool TryReadPathTargets(EntityManager entityManager, Entity segmentEntity, out RoutePathTargetPoints points, out string error)
        {
            points = default(RoutePathTargetPoints);
            error = string.Empty;

            try
            {
                ComponentType pathTargetsType;
                Type managedType;
                if (!TryFindComponentTypeByName(entityManager, segmentEntity, "PathTargets", false, out pathTargetsType, out managedType))
                {
                    error = $"segment {FormatEntity(segmentEntity)} has no Game.Routes.PathTargets component";
                    return false;
                }

                object pathTargets = ReadComponentValue(entityManager, segmentEntity, pathTargetsType, managedType);
                object startValue = ReadRequiredMember(pathTargets, managedType, "m_ReadyStartPosition");
                object endValue = ReadRequiredMember(pathTargets, managedType, "m_ReadyEndPosition");

                if (!TryReadXz(startValue, startValue.GetType(), out double startX, out double startZ))
                {
                    error = $"segment {FormatEntity(segmentEntity)} PathTargets.m_ReadyStartPosition did not expose x/z fields";
                    return false;
                }

                if (!TryReadXz(endValue, endValue.GetType(), out double endX, out double endZ))
                {
                    error = $"segment {FormatEntity(segmentEntity)} PathTargets.m_ReadyEndPosition did not expose x/z fields";
                    return false;
                }

                points = new RoutePathTargetPoints(startX, startZ, endX, endZ);
                return true;
            }
            catch (Exception ex)
            {
                error = $"failed to read PathTargets on segment {FormatEntity(segmentEntity)}: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static RoutePathPointExport CreatePathPoint(double x, double z, string source, Entity segmentEntity)
        {
            return new RoutePathPointExport
            {
                X = x,
                Z = z,
                Source = source,
                SegmentEntity = FormatEntity(segmentEntity)
            };
        }

        private static bool TryFindRouteSegmentBufferType(EntityManager entityManager, Entity lineEntity, out Type routeSegmentElementType)
        {
            routeSegmentElementType = null;

            try
            {
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

        private static bool TryFindComponentTypeByName(EntityManager entityManager, Entity entity, string componentName, bool? isBuffer, out ComponentType matchingType, out Type managedType)
        {
            matchingType = default(ComponentType);
            managedType = null;

            foreach (ComponentType componentType in GetComponentTypes(entityManager, entity))
            {
                if (isBuffer.HasValue && componentType.IsBuffer != isBuffer.Value)
                {
                    continue;
                }

                Type candidateType = SafeGetManagedType(componentType);
                string typeName = candidateType != null ? candidateType.FullName : componentType.ToString();
                if (typeName != null
                    && (string.Equals(typeName, "Game.Routes." + componentName, StringComparison.Ordinal)
                        || string.Equals(typeName, "Game.Pathfind." + componentName, StringComparison.Ordinal)
                        || typeName.EndsWith("." + componentName, StringComparison.Ordinal)
                        || typeName.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    matchingType = componentType;
                    managedType = candidateType;
                    return managedType != null;
                }
            }

            return false;
        }

        private static bool HasRouteGeometryComponent(EntityManager entityManager, Entity entity)
        {
            if (!IsValidEntity(entityManager, entity))
            {
                return false;
            }

            return TryFindComponentTypeByName(entityManager, entity, "CurveElement", null, out ComponentType _, out Type _)
                || TryFindComponentTypeByName(entityManager, entity, "PathElement", null, out ComponentType _, out Type _)
                || TryFindComponentTypeByName(entityManager, entity, "PathTargets", null, out ComponentType _, out Type _);
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

        private static object ReadRequiredMember(object value, Type valueType, string memberName)
        {
            object memberValue;
            Type memberType;
            if (TryReadMember(value, valueType, memberName, out memberValue, out memberType))
            {
                return memberValue;
            }

            throw new InvalidOperationException($"Member '{memberName}' was not found on {valueType.FullName}.");
        }

        private static bool TryReadMember(object value, Type valueType, string memberName, out object memberValue, out Type memberType)
        {
            memberValue = null;
            memberType = null;

            FieldInfo field = valueType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (field != null)
            {
                memberValue = field.GetValue(value);
                memberType = field.FieldType;
                return memberValue != null;
            }

                PropertyInfo property = valueType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                memberValue = property.GetValue(value, null);
                memberType = property.PropertyType;
                return memberValue != null;
            }

            return false;
        }

        private static bool TryReadXz(object value, Type valueType, out double x, out double z)
        {
            x = 0;
            z = 0;

            if (!TryReadNumericMember(value, valueType, "x", out x))
            {
                return false;
            }

            return TryReadNumericMember(value, valueType, "z", out z)
                || TryReadNumericMember(value, valueType, "y", out z);
        }

        private static bool TryReadStrictXz(object value, Type valueType, out double x, out double z)
        {
            x = 0;
            z = 0;

            return TryReadNumericMember(value, valueType, "x", out x)
                && TryReadNumericMember(value, valueType, "z", out z);
        }

        private static bool TryReadNumericMember(object value, Type valueType, string memberName, out double number)
        {
            number = 0;

            try
            {
                object memberValue = null;
                FieldInfo field = valueType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    memberValue = field.GetValue(value);
                }
                else
                {
                    PropertyInfo property = valueType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    {
                        memberValue = property.GetValue(value, null);
                    }
                }

                if (memberValue == null)
                {
                    return false;
                }

                number = Convert.ToDouble(memberValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<EntityReferenceInfo> FindEntityReferences(object value, Type valueType)
        {
            List<EntityReferenceInfo> references = new List<EntityReferenceInfo>();
            CollectEntityReferences(value, valueType, string.Empty, references, 0);

            Dictionary<string, EntityReferenceInfo> distinct = new Dictionary<string, EntityReferenceInfo>();
            foreach (EntityReferenceInfo reference in references)
            {
                string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", reference.Path, reference.Entity.Index, reference.Entity.Version);
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

            foreach (FieldInfo field in GetInstanceFields(valueType))
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

                string childPath = string.IsNullOrWhiteSpace(path) ? field.Name : path + "." + field.Name;
                if (field.FieldType == typeof(Entity))
                {
                    references.Add(new EntityReferenceInfo(childPath, (Entity)fieldValue));
                }
                else if (depth < 2 && ShouldInspectNestedType(field.FieldType))
                {
                    CollectEntityReferences(fieldValue, field.FieldType, childPath, references, depth + 1);
                }
            }

            foreach (PropertyInfo property in GetInstanceProperties(valueType))
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

                string childPath = string.IsNullOrWhiteSpace(path) ? property.Name : path + "." + property.Name;
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

            return type.IsValueType || (type.Namespace != null && (type.Namespace.StartsWith("Game.", StringComparison.Ordinal) || type.Namespace.StartsWith("Unity.", StringComparison.Ordinal) || type.Namespace.StartsWith("Colossal.", StringComparison.Ordinal)));
        }

        private static IEnumerable<FieldInfo> GetInstanceFields(Type type)
        {
            return type == null
                ? Enumerable.Empty<FieldInfo>()
                : type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static IEnumerable<PropertyInfo> GetInstanceProperties(Type type)
        {
            return type == null
                ? Enumerable.Empty<PropertyInfo>()
                : type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static string DumpCurveElement(object curveElement, Type curveElementType, object curveValue, Type curveType, string heading)
        {
            List<string> lines = new List<string>();
            lines.Add(heading);
            lines.Add($"CurveElement type: {FormatTypeName(curveElementType)}");
            lines.Add("CurveElement members:");
            lines.AddRange(DumpObjectMembers(curveElement, curveElementType, "  "));

            if (curveValue != null && curveType != null)
            {
                lines.Add($"Bezier/curve type: {FormatTypeName(curveType)}");
                lines.Add("Bezier/curve members:");
                lines.AddRange(DumpObjectMembers(curveValue, curveType, "  "));
            }

            return string.Join("; ", lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray());
        }

        private static IEnumerable<string> DumpObjectMembers(object value, Type valueType, string indent)
        {
            List<string> lines = new List<string>();
            if (valueType == null)
            {
                lines.Add($"{indent}type unavailable");
                return lines;
            }

            foreach (FieldInfo field in GetInstanceFields(valueType))
            {
                object memberValue = null;
                string summary;
                try
                {
                    memberValue = value == null ? null : field.GetValue(value);
                    summary = SafeFormatValue(memberValue);
                }
                catch (Exception ex)
                {
                    summary = $"Failed: {ex.GetType().Name}: {ex.Message}";
                }

                string line = $"{indent}field {field.Name} ({FormatTypeName(field.FieldType)}) = {summary}";
                string controlPoint = FormatControlPointIfNamed(field.Name, memberValue, field.FieldType);
                if (!string.IsNullOrWhiteSpace(controlPoint))
                {
                    line += $" [{controlPoint}]";
                }

                lines.Add(line);
            }

            foreach (PropertyInfo property in GetInstanceProperties(valueType))
            {
                if (property.GetIndexParameters().Length > 0)
                {
                    lines.Add($"{indent}property {property.Name} ({FormatTypeName(property.PropertyType)}) = indexed");
                    continue;
                }

                object memberValue = null;
                string summary;
                try
                {
                    memberValue = property.CanRead && value != null ? property.GetValue(value, null) : null;
                    summary = property.CanRead ? SafeFormatValue(memberValue) : "not readable";
                }
                catch (Exception ex)
                {
                    summary = $"Failed: {ex.GetType().Name}: {ex.Message}";
                }

                string line = $"{indent}property {property.Name} ({FormatTypeName(property.PropertyType)}) = {summary}";
                string controlPoint = FormatControlPointIfNamed(property.Name, memberValue, property.PropertyType);
                if (!string.IsNullOrWhiteSpace(controlPoint))
                {
                    line += $" [{controlPoint}]";
                }

                lines.Add(line);
            }

            if (lines.Count == 0)
            {
                lines.Add($"{indent}no instance fields or properties");
            }

            return lines;
        }

        private static string FormatControlPointIfNamed(string memberName, object memberValue, Type memberType)
        {
            if (!IsControlPointMemberName(memberName) || memberValue == null || memberType == null)
            {
                return string.Empty;
            }

            if (TryReadStrictXz(memberValue, memberType, out double x, out double z))
            {
                return $"controlPoint x={FormatDouble(x)}, z={FormatDouble(z)}";
            }

            return "controlPoint unreadable";
        }

        private static bool IsControlPointMemberName(string memberName)
        {
            string[] names = { "a", "b", "c", "d", "m_A", "m_B", "m_C", "m_D", "p0", "p1", "p2", "p3", "P0", "P1", "P2", "P3" };
            return names.Any(name => string.Equals(name, memberName, StringComparison.Ordinal));
        }

        private static string DumpMemberNames(Type type)
        {
            if (type == null)
            {
                return "type unavailable";
            }

            List<string> names = new List<string>();
            names.AddRange(GetInstanceFields(type).Select(field => $"field {field.Name}:{FormatTypeName(field.FieldType)}"));
            names.AddRange(GetInstanceProperties(type).Select(property => $"property {property.Name}:{FormatTypeName(property.PropertyType)}"));
            return JoinOrNone(names);
        }

        private static string FormatTypeName(Type type)
        {
            return type == null ? "unknown" : (type.FullName ?? type.Name);
        }

        private static bool IsGeometryMemberName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.IndexOf("bezier", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("curve", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("end", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPositionLikeType(Type type)
        {
            string typeName = type != null ? type.FullName ?? type.Name : string.Empty;
            return typeName.IndexOf("float3", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("double3", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("Position", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBezierLikeType(Type type)
        {
            string typeName = type != null ? type.FullName ?? type.Name : string.Empty;
            return typeName.IndexOf("Bezier", StringComparison.OrdinalIgnoreCase) >= 0
                || typeName.IndexOf("float4x3", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Entity ChooseSegmentEntity(EntityManager entityManager, List<EntityReferenceInfo> references)
        {
            foreach (EntityReferenceInfo reference in references)
            {
                if (HasRouteGeometryComponent(entityManager, reference.Entity))
                {
                    return reference.Entity;
                }
            }

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

        private static List<RoutePathPointExport> CleanupPathPoints(List<RoutePathPointExport> rawPathPoints)
        {
            List<RoutePathPointExport> cleaned = new List<RoutePathPointExport>();
            foreach (RoutePathPointExport point in rawPathPoints)
            {
                if (cleaned.Count == 0)
                {
                    cleaned.Add(point);
                    continue;
                }

                RoutePathPointExport previous = cleaned[cleaned.Count - 1];
                if (Math.Abs(previous.X - point.X) <= DeduplicationEpsilon && Math.Abs(previous.Z - point.Z) <= DeduplicationEpsilon)
                {
                    continue;
                }

                cleaned.Add(point);
            }

            return cleaned;
        }

        private static void AddMemberSummary(List<string> summaries, string summary)
        {
            if (summaries.Count < MaxMemberSummaries && !string.IsNullOrWhiteSpace(summary))
            {
                summaries.Add(summary);
            }
        }

        private static string SafeFormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            try
            {
                return value.ToString();
            }
            catch (Exception ex)
            {
                return $"format failed:{ex.GetType().Name}";
            }
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string JoinOrNone(IEnumerable<string> values)
        {
            string[] items = values == null
                ? new string[0]
                : values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            return items.Length == 0 ? "none" : string.Join("; ", items);
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

        private static bool IsValidEntity(EntityManager entityManager, Entity entity)
        {
            return entity != Entity.Null && entity.Index >= 0 && entityManager.Exists(entity);
        }

        private static string FormatEntity(Entity entity)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}", entity.Index, entity.Version);
        }

        private struct EntityReferenceInfo
        {
            public EntityReferenceInfo(string path, Entity entity)
            {
                Path = path;
                Entity = entity;
            }

            public string Path { get; private set; }

            public Entity Entity { get; private set; }
        }

        private struct RoutePathTargetPoints
        {
            public RoutePathTargetPoints(double startX, double startZ, double endX, double endZ)
            {
                StartX = startX;
                StartZ = startZ;
                EndX = endX;
                EndZ = endZ;
            }

            public double StartX { get; private set; }

            public double StartZ { get; private set; }

            public double EndX { get; private set; }

            public double EndZ { get; private set; }
        }

        public static List<RoutePathPointExport> CleanPathPointsForTesting(List<RoutePathPointExport> rawPathPoints)
        {
            return CleanupPathPoints(rawPathPoints);
        }
    }

    internal sealed class RoutePathPointExtractionResult
    {
        private const int MaxSkipReasons = 20;
        private const int MaxCurveElementFailures = 20;
        private const int MaxCurveElementDumps = 10;

        public List<RoutePathPointExport> PathPoints { get; private set; } = new List<RoutePathPointExport>();

        public int RouteSegmentCount { get; set; }

        public int CurveElementCount { get; set; }

        public int CurveSamplePointCount { get; set; }

        public int PathElementCount { get; set; }

        public int PathTargetsFallbackCount { get; set; }

        public int PathPointsBeforeCleanupCount { get; private set; }

        public int PathPointsAfterCleanupCount { get; private set; }

        public int SkippedSegmentCount { get; private set; }

        public string EmptyReason { get; set; }

        public List<string> SkipReasons { get; private set; } = new List<string>();

        public List<string> CurveElementFailures { get; private set; } = new List<string>();

        public List<string> CurveElementDumps { get; private set; } = new List<string>();

        public Dictionary<string, int> SourcePointCounts { get; private set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public void FinalizePathPoints(List<RoutePathPointExport> rawPathPoints)
        {
            PathPointsBeforeCleanupCount = rawPathPoints == null ? 0 : rawPathPoints.Count;
            PathPoints = rawPathPoints == null
                ? new List<RoutePathPointExport>()
                : RoutePathPointExtractor.CleanPathPointsForTesting(rawPathPoints);
            PathPointsAfterCleanupCount = PathPoints.Count;
            SourcePointCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RoutePathPointExport point in PathPoints)
            {
                string source = string.IsNullOrWhiteSpace(point.Source) ? "unknown" : point.Source;
                SourcePointCounts[source] = SourcePointCounts.ContainsKey(source) ? SourcePointCounts[source] + 1 : 1;
            }
        }

        public void RecordSkippedSegment(int segmentIndex, string reason)
        {
            SkippedSegmentCount++;
            if (SkipReasons.Count < MaxSkipReasons)
            {
                SkipReasons.Add(string.Format(CultureInfo.InvariantCulture, "segment[{0}]: {1}", segmentIndex, reason));
            }
        }

        public void RecordCurveElementFailure(int segmentIndex, Entity segmentEntity, string reason)
        {
            if (CurveElementFailures.Count < MaxCurveElementFailures)
            {
                CurveElementFailures.Add(string.Format(CultureInfo.InvariantCulture, "segment[{0}] {1}: {2}", segmentIndex, FormatEntityForResult(segmentEntity), reason));
            }
        }

        public void RecordCurveElementDump(int segmentIndex, int curveElementIndex, Entity segmentEntity, string dump)
        {
            if (CurveElementDumps.Count < MaxCurveElementDumps)
            {
                CurveElementDumps.Add(string.Format(CultureInfo.InvariantCulture, "segment[{0}] {1} CurveElement[{2}]: {3}", segmentIndex, FormatEntityForResult(segmentEntity), curveElementIndex, dump));
            }
        }

        private static string FormatEntityForResult(Entity entity)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}", entity.Index, entity.Version);
        }
    }

    internal sealed class RoutePathPointExport
    {
        public double X { get; set; }

        public double Z { get; set; }

        public string Source { get; set; }

        public string SegmentEntity { get; set; }
    }
}
