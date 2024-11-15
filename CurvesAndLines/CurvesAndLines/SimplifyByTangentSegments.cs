/* Copyright 2024 Esri
 *
 * Licensed under the Apache License Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Core.SystemCore;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CurvesAndLines
{
  internal class SimplifyByTangentSegments : Button
  {
    protected override async void OnClick()
    {
      //loop through all layers and get their selections
      CancelableProgressorSource cps = new("Simplify By Tangent Segments", "Canceled");
      string sReportResult = "";
      double userAllowedOffsetInMeters = 2;
      string errorMessage = await QueuedTask.Run(() =>
      {
        try
        {
          var lstFeatLayers =
            MapView.Active.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();

          Module1.GetFeatureLayerSelections(lstFeatLayers,
            out Dictionary<FeatureLayer, List<long>> layerIds);

          var editOper = new EditOperation()
          {
            Name = "Simplify By Tangent Segments",
            ShowModalMessageAfterFailure = true,
            ShowProgressor = true,
            SelectModifiedFeatures = false
          };
          int featureCount = 0;
          foreach (var layerId in layerIds)
          {
            var featLyr = layerId.Key;
            if (featLyr.ShapeType == esriGeometryType.esriGeometryPoint)
              continue; //skip point layers

            cps.Progressor.Message = "Simplifying " + featLyr.Name;
            var ids = new List<long>(layerId.Value);
            if (ids.Count == 0)
              continue;
            //return "No selected features found. Please select features and try again.";

            QueryFilter quFilter = new();
            quFilter.ObjectIDs = ids;

            var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
            var defn = featLyr.GetFeatureClass().GetDefinition();
            if (defn.GetSpatialReference().IsProjected)
              xyTol = defn.GetSpatialReference().XYTolerance;

            if (userAllowedOffsetInMeters <= 0.001)
              userAllowedOffsetInMeters = 0.001;

            if (userAllowedOffsetInMeters > 2.0 )
              userAllowedOffsetInMeters = 2.0;

            using RowCursor featCursor = featLyr.Search(quFilter);
            while (featCursor.MoveNext())
            {
              using Feature feature = (Feature)featCursor.Current;
              var featGeom = feature.GetShape();
              var origGeom = featGeom.Clone(); //Copy of the original geometry
              if (!SimplifyBySegmentTangency(ref featGeom, out int removedVertexCount1, xyTol * 1.4))//do an initial tangent-definitive run
                continue;
              if (SimplifyBySegmentTangency(ref featGeom, out int removedVertexCount2, userAllowedOffsetInMeters))
              {
                int removedVertexCount = removedVertexCount1 + removedVertexCount2;
                if (removedVertexCount > 0)
                {
                  if (!IsDifferentGeometry(userAllowedOffsetInMeters, origGeom, featGeom))
                  {
                    cps.Progressor.Value += (uint)removedVertexCount;
                    editOper.Modify(featLyr, feature.GetObjectID(), featGeom);
                    featureCount++;
                  }
                }
              }
              if (cps.Progressor.CancellationToken.IsCancellationRequested)
                break;
            }
            if (cps.Progressor.CancellationToken.IsCancellationRequested)
              break;

            cps.Progressor.Status = "Feature vertices removed: " + cps.Progressor.Value
            + Environment.NewLine + "Updating " + featureCount.ToString() + " features ...";
          }
          if (cps.Progressor.CancellationToken.IsCancellationRequested)
          {
            editOper.Abort();
            return "Canceled.";
          }
          if (!editOper.IsEmpty && cps.Progressor.Value > 0)
            editOper.Execute();
          layerIds.Clear();
        }
        catch (Exception ex)
        {
          return ex.Message;
        }
        return "";
      }, cps.Progressor);


      if (!string.IsNullOrEmpty(errorMessage))
        MessageBox.Show(errorMessage, "Simplify By Tangent Segments");
      else if (!string.IsNullOrEmpty(sReportResult))
        MessageBox.Show(sReportResult, "Simplify By Tangent Segments");
    }

    internal static bool IsDifferentGeometry(double AllowableDifferenceInMeters, Geometry originalGeometry, Geometry newGeometry)
    {
      ////==== test if removed vertices result in a geometry change that
      ////is more than the allowable offset tolerance
      bool outsideLimits = false;
      var mapPointList = GetGeometryCoordinates(originalGeometry);
      foreach (var mapPoint in mapPointList)
      {
        double d = GeometryEngine.Instance.Distance(mapPoint, newGeometry);
        if (d > AllowableDifferenceInMeters)
        {
          outsideLimits = true;
          break;
        }
      }
      return outsideLimits;
    }

    internal static List<MapPoint> GetGeometryCoordinates(Geometry geometry)
    {
      // Get the coordinates based on the geometry type
      List<MapPoint> mapPoints = new();
      switch (geometry.GeometryType)
      {
        case GeometryType.Point:
          MapPoint point = geometry as MapPoint;
          mapPoints.Add(point);
          break;
        case GeometryType.Polyline:
        case GeometryType.Polygon:
          foreach (var point2 in ((Multipart) geometry).Points)
            mapPoints.Add(point2);
          break;
          // Handle other geometry types as needed
        default:
            ;// Console.WriteLine("Unsupported geometry type.");
        break;
      }
      return mapPoints;
    }

    internal static bool SimplifyBySegmentTangency(ref Geometry theGeometry, out int VertexRemoveCount,
      double maxAllowedOffsetInMeters = 0.03)
    {
      VertexRemoveCount = 0;
      bool bSegmentsChanged = false;
      if (theGeometry == null)
        return false;

      var partCount = 1;
      var geomPerimLength = 0.0;

      //check for multi-parts and skip (not implemented)
      if (theGeometry is Polyline)
      {
        partCount = (theGeometry as Polyline).PartCount;
        geomPerimLength = (theGeometry as Polyline).Length;
      }
      if (theGeometry is Polygon)
      {
        partCount = (theGeometry as Polygon).PartCount;
        geomPerimLength = (theGeometry as Polygon).Length;
      }

      if (partCount > 1)
        return false;

      double _metersPerUnitDataset = 1.0;
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      if (theGeometry.SpatialReference.IsProjected)
      {
        xyTol = theGeometry.SpatialReference.XYTolerance;
        _metersPerUnitDataset = theGeometry.SpatialReference.Unit.ConversionFactor;
      }

      //test for special case where segments share common vertex, "pretzel" skip (not implemented)
      if (HasSegmentsSharingAVertex(theGeometry))
        return false;

      ICollection<Segment> geomSegments = new List<Segment>();
      if (theGeometry is Polyline)
        (theGeometry as Polyline).GetAllSegments(ref geomSegments);
      else if (theGeometry is Polygon)
        (theGeometry as Polygon).GetAllSegments(ref geomSegments);
      else
        return false;

      int iSegCount = geomSegments.Count;
      var lstLineSegments = geomSegments.ToList();
      if (iSegCount == 1)
        return true; //already simplified

      if (iSegCount == 0)
        return false; //no segments

      var longestSeg = 0.0;
      foreach (Segment segment in lstLineSegments)
        longestSeg = (segment.Length > longestSeg) ? segment.Length : longestSeg;

      var envHalfDiagLength = 
        Math.Sqrt(Math.Pow(theGeometry.Extent.Width, 2.0) + Math.Pow(theGeometry.Extent.Height, 2.0)) / 2.0 * _metersPerUnitDataset;

      for (int i = iSegCount - 1; i > 0; i--)
      {
        var pSeg1 = lstLineSegments[i];
        var pSeg0 = lstLineSegments[i - 1];

        //if a segment length is 0 then skip, the function removes them and saves the feature without
        //stacked vertices. Next time the zero length segments will not be present
        //if (pSeg1.Length == 0.0) - Length property bug for some geometry
        if (ChordDistance(pSeg1) < xyTol * 1.5)
          continue;

        //if (pSeg0.Length == 0.0) - Length property bug for some geometry
        if (ChordDistance(pSeg0) < xyTol * 1.5)
          continue;

        //test that the segments are connected within XY tolerance
        //and confirm that segments run head-to-toe.
        var pt01 = pSeg0.EndCoordinate;
        var pt02 = pSeg1.StartCoordinate;
        var dist = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
        if (dist > xyTol * 1.5)
          continue;

        //test that segments are not collapsed to the same point (side case for closed loop polylines)
        var pt03 = pSeg0.StartPoint;
        var pt04 = pSeg1.EndPoint;
        dist = Math.Sqrt(Math.Pow(pt03.X - pt04.X, 2.0) + Math.Pow(pt03.Y - pt04.Y, 2.0));
        if (dist < xyTol * 1.5)
          continue;

        bool Is2CircularArcs;
        bool Is2StraightLines;
        if (pSeg0 is EllipticArcSegment && pSeg1 is EllipticArcSegment)
        {
          Is2CircularArcs = true;
          Is2StraightLines = false;
          var pCirc0 = pSeg0 as EllipticArcSegment;
          var pCirc1 = pSeg1 as EllipticArcSegment;

          if (pCirc0.IsCounterClockwise && !pCirc1.IsCounterClockwise)
            continue;

          if (!pCirc0.IsCounterClockwise && pCirc1.IsCounterClockwise)
            continue;
        }
        else if (pSeg0.SegmentType == SegmentType.Line && pSeg1.SegmentType == SegmentType.Line)
        {
          Is2CircularArcs = false;
          Is2StraightLines = true;
        }
        else
        {
          Is2CircularArcs = false;
          Is2StraightLines = false;
        }

        bool segmentsAreTangent =
          IsSegmentPairTangent(pSeg0, pSeg1,MaxAllowedOffsetFromUserInMeters: maxAllowedOffsetInMeters,
              MaxFeatureLengthInMeters: envHalfDiagLength);

        if (segmentsAreTangent && Is2CircularArcs)
        {
          if (!HasSameCenterPoint(pSeg0, pSeg1))
            continue;

          var arcOr = ((EllipticArcSegment)pSeg0).IsCounterClockwise ? 
            ArcOrientation.ArcCounterClockwise: ArcOrientation.ArcClockwise;

          //Detect true elliptical arcs
           var trueEllipticalArcs = !(((EllipticArcSegment)pSeg0).IsCircular  && 
            ((EllipticArcSegment)pSeg1).IsCircular);

         var arcMinMaj =
            Math.Abs(((EllipticArcSegment)pSeg1).CentralAngle) + Math.Abs(((EllipticArcSegment)pSeg0).CentralAngle)
            < Math.PI ? MinorOrMajor.Minor: MinorOrMajor.Major;

          EllipticArcSegment longerSeg = pSeg0.Length > pSeg1.Length ? 
            (EllipticArcSegment)pSeg0 : (EllipticArcSegment)pSeg1;

          //use a circular arc constructor that ensures the start and end points are the same as for the original feature
          EllipticArcSegment pMergedCircularOrEllipticArc;

          if (!trueEllipticalArcs)
            pMergedCircularOrEllipticArc =
              EllipticArcBuilderEx.CreateCircularArc(pSeg0.StartPoint, pSeg1.EndPoint, longerSeg.CenterPoint, arcOr);
          else
            pMergedCircularOrEllipticArc =
            EllipticArcBuilderEx.CreateEllipticArcSegment(pSeg0.StartPoint, pSeg1.EndPoint,
            longerSeg.SemiMajorAxis,longerSeg.MinorMajorRatio,longerSeg.RotationAngle, arcMinMaj, arcOr);

          //Replace two segments with one
          lstLineSegments.RemoveRange(i - 1, 2);
          lstLineSegments.Insert(i - 1, pMergedCircularOrEllipticArc);
          bSegmentsChanged = true;
          VertexRemoveCount++;
        }

        if (segmentsAreTangent && Is2StraightLines)
        {
          var pMergedLine = LineBuilderEx.CreateLineSegment(pSeg0.StartPoint, pSeg1.EndPoint);
          //Replace two segments with one
          lstLineSegments.RemoveRange(i - 1, 2);
          lstLineSegments.Insert(i - 1, pMergedLine);
          bSegmentsChanged = true;
          VertexRemoveCount++;
        }
      }
      //update the geometry if the segments changed
      if (bSegmentsChanged)
      {
        bool bPartCountCheck;
        bool bPerimeterCheck;
        if (theGeometry is Polyline)
        {
          try
          { 
            var polyline = PolylineBuilderEx.CreatePolyline(lstLineSegments);
            if (!GeometryEngine.Instance.IsSimpleAsFeature(polyline))
              polyline = GeometryEngine.Instance.SimplifyAsFeature(polyline) as Polyline;
            bPartCountCheck = polyline.PartCount - partCount == 0;
            bPerimeterCheck = polyline.Length / geomPerimLength > 0.9;
            if(bPartCountCheck && bPerimeterCheck)
              theGeometry = polyline;
            else
              return false;
          }
          catch
          {
            return false;
          }
        }
        else if (theGeometry is Polygon)
        {
          try
          {
            var polygon = PolygonBuilderEx.CreatePolygon(lstLineSegments);
            if (!GeometryEngine.Instance.IsSimpleAsFeature(polygon))
              polygon = GeometryEngine.Instance.SimplifyAsFeature(polygon) as Polygon;
            bPartCountCheck = polygon.PartCount - partCount == 0;
            bPerimeterCheck = polygon.Length / geomPerimLength > 0.9;
            if (bPartCountCheck && bPerimeterCheck)
              theGeometry = polygon;
            else
              return false;
          }
          catch
          {//what cases cause CreatePolygon to fail?
            return false;
          }
        }
      }
      return true;
    }

    //private static Segment EntryTangentSegmentForCircularArc(EllipticArcSegment CircArc)
    //{
    //  double d90 = CircArc.IsCounterClockwise ? Math.PI / 2.0 : -Math.PI / 2.0;
    //  var tangentAngle = CircArc.StartAngle + d90;
    //  var nAzimuth = PolarRadiansToNorthAzimuthDecimalDegrees(tangentAngle);
    //  Coordinate2D tangentPoint = PointInDirection(CircArc.StartCoordinate, nAzimuth + 180.0, 100.0);
    //  var tangentSegment = LineBuilderEx.CreateLineSegment(tangentPoint, CircArc.StartCoordinate);
    //  return tangentSegment;
    //}

    private static double ChordDistance(Segment segment)
    {
      if (segment == null)
        return 0.0;
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      return Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
    }

    private static Segment ShortCircularArcSegmentCheckAndRepair(EllipticArcSegment segment, double xyTol)
    {
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      var chord = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));

      if (segment.Length == 0.0 && chord > xyTol)//this should never be true ... geometry bug 
      {
        var point1=pt01.ToMapPoint();
        var point2=pt02.ToMapPoint();
        var flatCircularArcRadius = chord / (0.1 * Math.PI / 180.0); //based on 0.1 degree central angle
        var flatCircArc = CreateCircularArcByEndpoints(point1, point2, flatCircularArcRadius, segment.IsCounterClockwise, false);
        if (flatCircArc != null)
          return flatCircArc;
      }
      return segment;
    }

    internal static EllipticArcSegment CreateCircularArcByEndpoints(MapPoint StartPoint, MapPoint EndPoint,
      double Radius, bool IsCounterClockwise, bool IsMajor, double ScaleFactorForRadius = 1.0)
    {
      var pNewSeg = LineBuilderEx.CreateLineSegment(StartPoint, EndPoint);

      double chordDirection = pNewSeg.Angle;
      double dChord = pNewSeg.Length;

      ArcOrientation CCW =
        IsCounterClockwise ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
      MinorOrMajor minMaj =
        IsMajor ? MinorOrMajor.Major : MinorOrMajor.Minor;

      double dRadius = Math.Abs(Radius) * ScaleFactorForRadius;

      EllipticArcSegment circArcSegment;
      try
      {
        circArcSegment = EllipticArcBuilderEx.CreateCircularArc(StartPoint, dChord, chordDirection,
        dRadius, CCW, minMaj, null);
        return circArcSegment;
      }
      catch { return null; }
    }

    internal static bool IsSegmentPairTangentByAngleTolerance(Segment seg1, Segment seg2, double AngleToleranceInSeconds = 1800)
    {
      //the azimuth difference method is weak since the angles that define
      //valid tangency are dependent on the segment lengths.
      //There are other approaches that take into account relative segment
      //lengths such as using offset distances. See function IsSegmentPairTangentByOffsetRatio

      var pLineVec0 = GeometryEngine.Instance.QueryTangent(seg1, SegmentExtensionType.ExtendTangentAtTo,
        1.0, AsRatioOrLength.AsRatio, 10000.0);

      var pLineVec1 = GeometryEngine.Instance.QueryTangent(seg2, SegmentExtensionType.ExtendTangentAtFrom,
        0.0, AsRatioOrLength.AsRatio, 10000.0);
      
      var Vector0 = new Coordinate3D();
      Vector0.SetPolarComponents(pLineVec0.Angle, 0.0, 1.0);

      var Vector1 = new Coordinate3D();
      Vector1.SetPolarComponents(pLineVec1.Angle, 0.0, 1.0);

      double dVectDiffInDecDegs = 180.0 / Math.PI * Math.Acos(Vector0.DotProduct(Vector1));
      bool areTangent = dVectDiffInDecDegs < AngleToleranceInSeconds / 3600.0;
      return areTangent;
    }

    internal static bool HasSameCenterPoint(Segment seg1, Segment seg2)
    {
      if (seg1.SegmentType!=SegmentType.EllipticArc || seg2.SegmentType != SegmentType.EllipticArc)
        return false;

      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree

      if (seg1.SpatialReference.IsProjected)
        xyTol = seg1.SpatialReference.XYTolerance;

      double baseTolerance = xyTol * 30.0;
      var shorterCircArc = seg1.Length<=seg2.Length?seg1 as EllipticArcSegment: seg2 as EllipticArcSegment;

      double circArcDelta = shorterCircArc.CentralAngle;

      //check for flat circular arcs central angle less than 1°, length less than 1 meter, with same CW/CCW orientation
      //return center point = true, since prior functions
      if (Math.Abs(shorterCircArc.CentralAngle) < (1.0 / 180.0 * Math.PI) && ChordDistance(shorterCircArc) < xyTol*1000)
        return (seg1 as EllipticArcSegment).IsCounterClockwise == (seg2 as EllipticArcSegment).IsCounterClockwise;

      //take care of edge cases -3° to 3° and 177° to 183°
        if (Math.Abs(circArcDelta - Math.PI) < (3.0 * Math.PI / 180.0) || 
            Math.Abs(circArcDelta) < (3.0 * Math.PI / 180.0))
        circArcDelta = 3.0 * Math.PI / 180.0;

      //use a precision noise as 1.25 % of radius
      double d1Percent = Math.Abs(shorterCircArc.SemiMajorAxis) * (1.25/ 100.0);
      double precisionNoiseFactor = d1Percent * Math.Cos(circArcDelta); //maximized for small central angles
      double radiusTolerance = baseTolerance + precisionNoiseFactor;
      if (radiusTolerance < xyTol)
        radiusTolerance = xyTol;

      var r1 = Math.Abs((seg1 as EllipticArcSegment).SemiMajorAxis);
      var r2 = Math.Abs((seg2 as EllipticArcSegment).SemiMajorAxis);
      double testRadiusDifference = Math.Abs(r2 - r1);

      //test the distance between center points to confirm the same side of circular arc
       //use 10 percent of radius; only a course-grained check needed
      double centerPointTolerance = 10.0 * d1Percent;
      var cp1 = (seg1 as EllipticArcSegment).CenterPoint;
      var cp2 = (seg2 as EllipticArcSegment).CenterPoint;
      var testDist = LineBuilderEx.CreateLineSegment(cp1, cp2).Length;

      return testRadiusDifference <= radiusTolerance && testDist <= centerPointTolerance;
    }

    internal static bool IsSegmentPairTangent(Segment seg1, Segment seg2, double MaxAllowedOffsetFromUserInMeters = 0.03,
      double MinOffsetToleranceInMeters = 0.0, double MaxOffsetToleranceInMeters = 0.0,
      double MaxFeatureLengthInMeters = 0.0, double OffsetRatio = 250.0)
    {
      //MaxAllowedOffsetFromUserInMeters: This user provided tolerance is the master / override.
      //No geometry change more than this allowed.
      //The other tolerances and offset ratio parameters in this function are "under-the-hood"
      //tuning settings, ideally never exposed to the user.

      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      double _metersPerUnitDataset = 1.0;
      //Convert all lengths to meters

      if (seg1.SpatialReference.IsProjected)
      {
        xyTol = seg1.SpatialReference.XYTolerance;
        _metersPerUnitDataset = seg1.SpatialReference.Unit.ConversionFactor;
      }

      // ---- Workaround for geometry bug 3.0 and 3.1 ----
      if (seg1 is EllipticArcSegment)
        seg1 = ShortCircularArcSegmentCheckAndRepair(seg1 as EllipticArcSegment, xyTol);
      if (seg2 is EllipticArcSegment)
        seg2 = ShortCircularArcSegmentCheckAndRepair(seg2 as EllipticArcSegment, xyTol);
      //---------
      
      if (MaxFeatureLengthInMeters == 0.0)
        MaxFeatureLengthInMeters = seg1.Length >= seg2.Length ? seg1.Length * _metersPerUnitDataset : seg2.Length * _metersPerUnitDataset;

      if (seg1 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg1 = GeometryEngine.Instance.QueryTangent(seg1, SegmentExtensionType.ExtendTangentAtTo,
          1.0, AsRatioOrLength.AsRatio, seg1.Length * _metersPerUnitDataset);
        var ln = LineBuilderEx.CreateLineSegment(seg1.StartPoint, seg1.EndPoint);
        var newStartPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(seg1.StartPoint, 
          ln.Angle + Math.PI,seg1.Length * _metersPerUnitDataset);
        seg1 = LineBuilderEx.CreateLineSegment(newStartPoint, seg1.StartPoint);
      }
      if (seg2 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg2 = GeometryEngine.Instance.QueryTangent(seg2, SegmentExtensionType.ExtendTangentAtFrom,
          0.0, AsRatioOrLength.AsRatio, seg2.Length * _metersPerUnitDataset);
      }
      var minO = xyTol * 20.0 * _metersPerUnitDataset; // 2cms default
      var maxO = xyTol * 2000.0 * _metersPerUnitDataset; // 200cms default

      var oRP = Math.Abs(OffsetRatio);
      if (oRP < 10.0)
        oRP = 10.0;

      if (MinOffsetToleranceInMeters != 0.0)
        minO = MinOffsetToleranceInMeters;

      if (MaxOffsetToleranceInMeters != 0.0)
        maxO = MaxOffsetToleranceInMeters;

      var pointA = seg1.StartCoordinate;
      var pointB = seg1.EndCoordinate;
      var pointC = seg2.EndCoordinate;

      //straight line ac (long line)
      var lineACVec = new Coordinate3D(pointC.X - pointA.X, pointC.Y - pointA.Y, 0.0);
      var lineACUnitVec = new Coordinate3D();//unit vector
      lineACUnitVec.SetPolarComponents(lineACVec.Azimuth, 0.0, 1.0);
      //====
      //straight line ab (short line a to test point b)
      var lineABVec = new Coordinate3D(pointB.X - pointA.X, pointB.Y - pointA.Y, 0);
      var lineABUnitVec = new Coordinate3D();//unit vector
      lineABUnitVec.SetPolarComponents(lineABVec.Azimuth, 0.0, 1.0);
      //====
      //straight line bc (short line c to test point b)
      var lineBCVec = new Coordinate3D(pointC.X - pointB.X, pointC.Y - pointB.Y, 0.0);
      var lineBCUnitVec = new Coordinate3D();//unit vector
      lineBCUnitVec.SetPolarComponents(lineBCVec.Azimuth, 0.0, 1.0);
      //====

      var dotProd = lineACUnitVec.DotProduct(lineABUnitVec);
      var angBAC = Math.Acos(dotProd);

      var mB = Math.Abs(Math.Sin(angBAC) * lineABVec.Magnitude * _metersPerUnitDataset);
      var mA = Math.Abs(Math.Cos(angBAC) * lineABVec.Magnitude * _metersPerUnitDataset);

      if (mB <= xyTol/1.1)
        return true; //if perpendicular offset distance is near-zero, then segments are tangent

      var maxAllowableOffsetFromUser = xyTol * 30.0 * _metersPerUnitDataset; // 3cms default
      if (MaxAllowedOffsetFromUserInMeters != 0.03)
        maxAllowableOffsetFromUser = xyTol * MaxAllowedOffsetFromUserInMeters*1000.0 * _metersPerUnitDataset;

      if (mB > maxAllowableOffsetFromUser)
        return false; //iAmTangent? = no

      dotProd = lineACUnitVec.DotProduct(lineBCUnitVec);
      var angBCA = Math.Acos(dotProd);

      //Check for segment deflections between 45° and 135°
      //Considered to be a bend regardless of segment lengths
      var angBACinDeg = Math.Abs(angBAC) * 180.0 / Math.PI;
      var angBCAinDeg = Math.Abs(angBCA) * 180.0 / Math.PI;

      if (angBACinDeg > 45.0 && angBACinDeg < 135.0)
        return false;
      if (angBCAinDeg > 45.0 && angBCAinDeg < 135.0)
        return false;

      var mC = Math.Abs(Math.Cos(angBCA) * lineBCVec.Magnitude * _metersPerUnitDataset);
      var dAC = lineACVec.Magnitude * _metersPerUnitDataset;
      var z = (MaxFeatureLengthInMeters > dAC) ? Math.Log(MaxFeatureLengthInMeters / dAC) : Math.Log(MaxFeatureLengthInMeters * dAC);
      var lengthContextMinO = minO * z;

      if (lengthContextMinO < minO / 2.0)
        lengthContextMinO = minO / 2.0; // 1 cm is the smallest allowable offset. Clamp to smallest

      var R = (mC < mA) ? mC / mB : mA / mB;
      bool iAmABend = (R <= oRP && mB >= lengthContextMinO) || mB >= maxO;
      return !iAmABend; //tangent if not a bend
    }

    internal static bool HasSegmentsSharingAVertex(Geometry geom)
    {
      //test for special case where segments share common vertex, "pretzel"
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      double _metersPerUnitDataset = 1.0;

      string sDecPrecFmt = "F9";

      if (geom.SpatialReference.IsProjected)
      {
        xyTol = geom.SpatialReference.XYTolerance;
        _metersPerUnitDataset = geom.SpatialReference.Unit.ConversionFactor;
        sDecPrecFmt = "F3";
      }

      ReadOnlyPointCollection ptColl = null;
      if (geom is Polyline)
        ptColl = (geom as Polyline).Points;
      else if (geom is Polygon)
        ptColl = (geom as Polygon).Points;
      else
        return false;
      
      List<string> lst = new();
      foreach(var pt in ptColl)
        lst.Add(pt.X.ToString(sDecPrecFmt) + "," + pt.Y.ToString(sDecPrecFmt) );

      lst.RemoveAt(lst.Count - 1);
      lst.RemoveAt(0);

      var query = lst.GroupBy(x => x)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

      return query.Count > 0;
    }
  }
}


  
