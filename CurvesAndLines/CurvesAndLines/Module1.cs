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
using System.Windows.Input;

namespace CurvesAndLines
{
  internal class Module1 : Module
  {
    private static Module1 _this = null;

    /// <summary>
    /// Retrieve the singleton instance to this module here
    /// </summary>
    public static Module1 Current
    {
      get
      {
        return _this ?? (_this = (Module1)FrameworkApplication.FindModule("CurvesAndLines_Module"));
      }
    }

    #region Overrides
    /// <summary>
    /// Called by Framework when ArcGIS Pro is closing
    /// </summary>
    /// <returns>False to prevent Pro from closing, otherwise True</returns>
    protected override bool CanUnload()
    {
      //TODO - add your business logic
      //return false to ~cancel~ Application close
      return true;
    }

    #endregion Overrides

    internal static bool HasSameCenterPoint(Segment seg1, Segment seg2, double precisionNoise = 1.25)
    {
      if (seg1.SegmentType != SegmentType.EllipticArc || seg2.SegmentType != SegmentType.EllipticArc)
        return false;

      if (precisionNoise <= 1.1)
        precisionNoise = 1.25;

      if (precisionNoise > 1.4)
        precisionNoise = 1.25;

      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree

      if (seg1.SpatialReference.IsProjected)
        xyTol = seg1.SpatialReference.XYTolerance;

      double baseTolerance = xyTol * 30.0;
      var shorterCircArc = seg1.Length <= seg2.Length ? seg1 as EllipticArcSegment : seg2 as EllipticArcSegment;

      double circArcDelta = shorterCircArc.CentralAngle;

      //check for flat circular arcs central angle less than 1°, length less than 1 meter, with same CW/CCW orientation
      //return center point = true, since prior functions
      if (Math.Abs(shorterCircArc.CentralAngle) < (1.0 / 180.0 * Math.PI) && ChordDistance(shorterCircArc) < xyTol * 1000)
        return (seg1 as EllipticArcSegment).IsCounterClockwise == (seg2 as EllipticArcSegment).IsCounterClockwise;

      //take care of edge cases -3° to 3° and 177° to 183°
      if (Math.Abs(circArcDelta - Math.PI) < (3.0 * Math.PI / 180.0) ||
          Math.Abs(circArcDelta) < (3.0 * Math.PI / 180.0))
        circArcDelta = 3.0 * Math.PI / 180.0;

      //use a precision noise as 1.25 % of radius
      double d1Percent = Math.Abs(shorterCircArc.SemiMajorAxis) * (precisionNoise / 100.0);
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
    internal static bool IsShortFlatCircularArcSegment(EllipticArcSegment segment, double ArcLengthTolerance, double xyTol)
    {
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      var chord = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));

      if (segment.Length == 0.0 && chord > xyTol) //this should never be true ... geometry bug 3.0 and 3.1
      {
        var point1 = pt01.ToMapPoint();
        var point2 = pt02.ToMapPoint();
        var flatCircularArcRadius = chord / (0.1 * Math.PI / 180.0); //based on 0.1 degree central angle
        var flatCircArc = CreateCircularArcByEndpoints(point1, point2, flatCircularArcRadius, segment.IsCounterClockwise, false);
        if (flatCircArc != null)
          return true;
      }

      if (segment.Length < ArcLengthTolerance && chord < ArcLengthTolerance) //
      {
        var point1 = pt01.ToMapPoint();
        var point2 = pt02.ToMapPoint();
        var flatCircularArcRadius = chord / (0.1 * Math.PI / 180.0); //based on 0.1 degree central angle
        var flatCircArc = CreateCircularArcByEndpoints(point1, point2, flatCircularArcRadius, segment.IsCounterClockwise, false);
        if (flatCircArc != null)
          return true;
      }

      return false;
    }

    private static double ChordDistance(Segment segment)
    {
      if (segment == null)
        return 0.0;
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      return Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
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
      foreach (var pt in ptColl)
        lst.Add(pt.X.ToString(sDecPrecFmt) + "," + pt.Y.ToString(sDecPrecFmt));

      lst.RemoveAt(lst.Count - 1);
      lst.RemoveAt(0);

      var query = lst.GroupBy(x => x)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

      return query.Count > 0;
    }

    internal static bool IsSegmentPairTangent(Segment seg1, Segment seg2, double MaxAllowedOffsetFromUserInMeters = 2.0,
      double MinOffsetToleranceInMeters = 0.02, double MaxOffsetToleranceInMeters = 2.0,
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

      if (MaxFeatureLengthInMeters <= 0.0)
        MaxFeatureLengthInMeters = seg1.Length >= seg2.Length ? seg1.Length * _metersPerUnitDataset : seg2.Length * _metersPerUnitDataset;

      if (seg1 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg1 = GeometryEngine.Instance.QueryTangent(seg1, SegmentExtensionType.ExtendTangentAtTo,
          1.0, AsRatioOrLength.AsRatio, seg1.Length * _metersPerUnitDataset);
        var ln = LineBuilderEx.CreateLineSegment(seg1.StartPoint, seg1.EndPoint);
        var newStartPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(seg1.StartPoint,
          ln.Angle + Math.PI, seg1.Length * _metersPerUnitDataset);
        seg1 = LineBuilderEx.CreateLineSegment(newStartPoint, seg1.StartPoint);
      }
      if (seg2 is EllipticArcSegment) //convert it to tangent line segment equivalent
      {
        seg2 = GeometryEngine.Instance.QueryTangent(seg2, SegmentExtensionType.ExtendTangentAtFrom,
          0.0, AsRatioOrLength.AsRatio, seg2.Length * _metersPerUnitDataset);
      }

      if (MinOffsetToleranceInMeters <= 0.0 || MinOffsetToleranceInMeters >= MaxOffsetToleranceInMeters)
        MinOffsetToleranceInMeters = xyTol * 20.0 * _metersPerUnitDataset; // 2cms default 

      if (MaxOffsetToleranceInMeters <= 0.0 || MaxOffsetToleranceInMeters <= MinOffsetToleranceInMeters)
        MaxOffsetToleranceInMeters = xyTol * 2000.0 * _metersPerUnitDataset; // 200cms default 

      MaxAllowedOffsetFromUserInMeters = Math.Abs(MaxAllowedOffsetFromUserInMeters);

      if (MaxAllowedOffsetFromUserInMeters > MaxOffsetToleranceInMeters)
        MaxAllowedOffsetFromUserInMeters = MaxOffsetToleranceInMeters;

      var minO = MinOffsetToleranceInMeters; //xyTol * 20.0 * _metersPerUnitDataset; // 2cms default
      var maxO = MaxOffsetToleranceInMeters;//xyTol * 2000.0 * _metersPerUnitDataset; // 200cms default

      var oRP = Math.Abs(OffsetRatio);
      if (oRP < 10.0)
        oRP = 10.0;

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

      if (mB <= xyTol / 1.1)
        return true; //if perpendicular offset distance is near-zero, then segments are tangent

      var maxAllowableOffsetFromUser = xyTol * 30.0 * _metersPerUnitDataset; // 3cms default
      if (MaxAllowedOffsetFromUserInMeters != 0.03)
        maxAllowableOffsetFromUser = xyTol * MaxAllowedOffsetFromUserInMeters * 1000.0 * _metersPerUnitDataset;

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

    private static Segment ShortCircularArcSegmentCheckAndRepair(EllipticArcSegment segment, double xyTol)
    {
      var pt01 = segment.StartCoordinate;
      var pt02 = segment.EndCoordinate;
      var chord = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));

      if (segment.Length == 0.0 && chord > xyTol)//this should never be true ... geometry bug 
      {
        var point1 = pt01.ToMapPoint();
        var point2 = pt02.ToMapPoint();
        var flatCircularArcRadius = chord / (0.1 * Math.PI / 180.0); //based on 0.1 degree central angle
        var flatCircArc = CreateCircularArcByEndpoints(point1, point2, flatCircularArcRadius, segment.IsCounterClockwise, false);
        if (flatCircArc != null)
          return flatCircArc;
      }
      return segment;
    }

    internal static bool SimplifyBySegmentTangencyEx(ref Geometry theGeometry, out int VertexRemoveCount, out int BendCount,
          double maxAllowedOffsetInMeters = 2.0)
    {
      //Short and flat circular arc segment parameter detection settings
      double circularArcRadiusPrecisionNoise = 1.25; //used to determine if circular arcs share a common centerpoint
      double flatShortCircularArcToleranceFactor = 50.0; //multiplied by XY tolerance, 50 represents 5cms arclength
      //
      BendCount = 0;
      VertexRemoveCount = 0;
      bool bSegmentsChanged = false;
      if (theGeometry == null)
        return false;

      double _metersPerUnitDataset = 1.0;
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      var spatRef = theGeometry.SpatialReference;
      if (spatRef.IsProjected)
      {
        xyTol = spatRef.XYTolerance;
        _metersPerUnitDataset = spatRef.Unit.ConversionFactor;
      }
      //var partCount = 1;
      var geomPerimLength = 0.0;

      //check for multi-parts and skip (not implemented)
      if (theGeometry is Polyline)
      {
        //partCount = (theGeometry as Polyline).PartCount;
        geomPerimLength = (theGeometry as Polyline).Length;
      }
      if (theGeometry is Polygon)
      {
        //partCount = (theGeometry as Polygon).PartCount;
        geomPerimLength = (theGeometry as Polygon).Length;
      }

      // Get the AttributeFlags enumeration values
      AttributeFlags attributeFlags = AttributeFlags.None;
      if (theGeometry.HasID)
        attributeFlags = AttributeFlags.HasID;
      if (theGeometry.HasZ)
        attributeFlags |= AttributeFlags.HasZ;
      if (theGeometry.HasM)
        attributeFlags |= AttributeFlags.HasM;

      List<List<Segment>> newFeatureSegments = new();

      switch (theGeometry.GeometryType)
      {
        case GeometryType.Point:
          break;
        case GeometryType.Polyline:
        case GeometryType.Polygon:
          var parts = ((Multipart)theGeometry).Parts;
          foreach (var partSegments in parts)
          {
            GetPolylineFromSegments(partSegments, out Polyline polylineForThisPart);
            if (polylineForThisPart == null)
              continue;

            var lstPartSegments = partSegments.ToList();

            if (theGeometry is Polygon)
            {
              int iPos = FindLongestSegment(lstPartSegments);
              ResequenceSegments(ref lstPartSegments, iPos);
            }

            //test for special case where segments in the part share common vertex, skip "pretzels" (not implemented)
            if (HasSegmentsSharingAVertex2(partSegments))
              return false; //Currently code bails completely on this feature.
            //TODO: hold on to the pretzels and keep processing the other parts.
            //continue;

            int iSegCount = partSegments.Count; //reset the count for this ring of segments
            if (iSegCount == 1)
            {//already a single segment in this part, but first add it to be used
             //in the new geometry constructor, before continuing to next part
              newFeatureSegments.Add(lstPartSegments);
              continue;
            }

            if (iSegCount == 0)
              continue; //no segments

            var longestSeg = 0.0;
            foreach (Segment segment in partSegments)
              longestSeg = (segment.Length > longestSeg) ? segment.Length : longestSeg;

            var envHalfDiagLength =
              Math.Sqrt(Math.Pow(polylineForThisPart.Extent.Width, 2.0) + Math.Pow(polylineForThisPart.Extent.Height, 2.0)) / 2.0 * _metersPerUnitDataset;

            #region run the segment pair analysis for this part

            for (int i = iSegCount - 1; i > 0; i--)
            {//need to edit the list as we go, so make sure to read the changing list lstPartSegments, and not the read-only list
              var pSeg1 = lstPartSegments[i];
              var pSeg0 = lstPartSegments[i - 1];

              //if a segment length is 0 then skip, the function removes them and saves the feature without
              //stacked vertices.
              //if (pSeg1.Length == 0.0) - Length property bug for some geometry
              if (ChordDistance(pSeg1) < xyTol * 1.5)
                continue;

              //if (pSeg0.Length == 0.0) - Length property bug for some geometry
              if (ChordDistance(pSeg0) < xyTol * 1.5)
                continue;

              //test that the segments are connected within XY tolerance
              //and confirm that segments run head-to-toe.
              //--Guard rail - consider removing this code as it less applicable now for multi-part--
              //  Should never have an issue here
              var pt01 = pSeg0.EndCoordinate;
              var pt02 = pSeg1.StartCoordinate;
              var dist = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
              if (dist > xyTol * 1.5)
                continue;
              //---------------------------------------

              //test that segments are not collapsed to the same point (side case for closed loop polylines)
              //--Guard rail
              var pt03 = pSeg0.StartPoint;
              var pt04 = pSeg1.EndPoint;
              dist = Math.Sqrt(Math.Pow(pt03.X - pt04.X, 2.0) + Math.Pow(pt03.Y - pt04.Y, 2.0));
              if (dist < xyTol * 1.5)
                continue;
              //---------------------------------------

              bool Is2CircularArcs;
              bool Is2StraightLines;
              if (pSeg0 is EllipticArcSegment && pSeg1 is EllipticArcSegment)
              {
                Is2CircularArcs = true;
                Is2StraightLines = false;
                var pCirc0 = pSeg0 as EllipticArcSegment;
                var pCirc1 = pSeg1 as EllipticArcSegment;

                if (!Module1.IsShortFlatCircularArcSegment(pCirc0, flatShortCircularArcToleranceFactor, xyTol) &&
                  !Module1.IsShortFlatCircularArcSegment(pCirc1, flatShortCircularArcToleranceFactor, xyTol))
                {
                  if (pCirc0.IsCounterClockwise && !pCirc1.IsCounterClockwise)
                    continue;

                  if (!pCirc0.IsCounterClockwise && pCirc1.IsCounterClockwise)
                    continue;
                }
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
                Module1.IsSegmentPairTangent(pSeg0, pSeg1, MaxAllowedOffsetFromUserInMeters: maxAllowedOffsetInMeters,
                    MaxFeatureLengthInMeters: envHalfDiagLength);
              
              if (!segmentsAreTangent)
                BendCount++;
              
              if (segmentsAreTangent && Is2CircularArcs)
              {
                if (!Module1.HasSameCenterPoint(pSeg0, pSeg1, circularArcRadiusPrecisionNoise) &&
                  !Module1.IsShortFlatCircularArcSegment(pSeg0 as EllipticArcSegment, flatShortCircularArcToleranceFactor, xyTol) &&
                  !Module1.IsShortFlatCircularArcSegment(pSeg1 as EllipticArcSegment, flatShortCircularArcToleranceFactor, xyTol))
                  continue;

                var arcOr = ((EllipticArcSegment)pSeg0).IsCounterClockwise ?
                  ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;

                //Detect true elliptical arcs
                var trueEllipticalArcs = !(((EllipticArcSegment)pSeg0).IsCircular &&
                 ((EllipticArcSegment)pSeg1).IsCircular);

                var arcMinMaj =
                   Math.Abs(((EllipticArcSegment)pSeg1).CentralAngle) + Math.Abs(((EllipticArcSegment)pSeg0).CentralAngle)
                   < Math.PI ? MinorOrMajor.Minor : MinorOrMajor.Major;

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
                  longerSeg.SemiMajorAxis, longerSeg.MinorMajorRatio, longerSeg.RotationAngle, arcMinMaj, arcOr);

                //Replace two segments with one
                lstPartSegments.RemoveRange(i - 1, 2);
                lstPartSegments.Insert(i - 1, pMergedCircularOrEllipticArc);
                bSegmentsChanged = true;
                VertexRemoveCount++;
              }

              if (segmentsAreTangent && Is2StraightLines)
              {
                var pMergedLine = LineBuilderEx.CreateLineSegment(pSeg0.StartPoint, pSeg1.EndPoint);
                //Replace two segments with one
                lstPartSegments.RemoveRange(i - 1, 2);
                lstPartSegments.Insert(i - 1, pMergedLine);
                bSegmentsChanged = true;
                VertexRemoveCount++;
              }
            }
            #endregion

            if (theGeometry is Polygon)
            {//Special case:
              //Check the last and first segment on this part for tangency
              //first convert polylineForThisPart to a polygon
              var updatedPolygonForThisPart = PolygonBuilderEx.CreatePolygon(lstPartSegments, spatRef) as Geometry;

              if (!GeometryEngine.Instance.IsSimpleAsFeature(updatedPolygonForThisPart))
                updatedPolygonForThisPart = GeometryEngine.Instance.SimplifyAsFeature(updatedPolygonForThisPart) as Polygon;

              if (SimplifyPolygonByLastAndFirstSegmentTangency(ref updatedPolygonForThisPart, out List<Segment> lstPolygonPartSegments))
              {
                bSegmentsChanged = true;
                int i = lstPartSegments.Count - lstPolygonPartSegments.Count;
                VertexRemoveCount += i;
                lstPartSegments = lstPolygonPartSegments;
              }
            }

            newFeatureSegments.Add(lstPartSegments);

          } //end partsegments loop
          break;
        default:
          ;// Unsupported geometry type.
          break;
      }

      // now reconstruct the new geometry with all parts if the segments changed
      if (bSegmentsChanged)
      {
        bool bPerimeterCheck;
        if (theGeometry is Polygon)
        {
          try
          {
            // Create a polygon builder
            PolygonBuilderEx polygonBuilder = new(newFeatureSegments, attributeFlags, spatRef);
            // Build the polygon
            Polygon updatedPolygon = polygonBuilder.ToGeometry();
            if (!GeometryEngine.Instance.IsSimpleAsFeature(updatedPolygon))
              updatedPolygon = GeometryEngine.Instance.SimplifyAsFeature(updatedPolygon) as Polygon;
            var ratioPerimeter = updatedPolygon.Length < geomPerimLength ?
              updatedPolygon.Length / geomPerimLength : geomPerimLength / updatedPolygon.Length;
            bPerimeterCheck = ratioPerimeter > 0.9;
            if (bPerimeterCheck)
              theGeometry = updatedPolygon;
            else
              return false; //what cases cause CreatePolygon to fail?
          }
          catch
          {
            return false;
          }
        }
        else //polyline
        {
          try
          {
            // Create a polyline builder
            PolylineBuilderEx polylineBuilder = new(newFeatureSegments, attributeFlags, spatRef);
            // Build the polyline
            Polyline updatedPolyline = polylineBuilder.ToGeometry();
            if (!GeometryEngine.Instance.IsSimpleAsFeature(updatedPolyline))
              updatedPolyline = GeometryEngine.Instance.SimplifyAsFeature(updatedPolyline) as Polyline;
            var ratioPerimeter = updatedPolyline.Length < geomPerimLength ?
              updatedPolyline.Length / geomPerimLength : geomPerimLength / updatedPolyline.Length;
            bPerimeterCheck = ratioPerimeter > 0.9;
            if (bPerimeterCheck)
              theGeometry = updatedPolyline;
            else
              return false; //what cases cause CreatePolyline to fail?
          }
          catch
          {
            return false;
          }
        }
      }
      return true;
    }


    internal static void ResequenceSegments(ref List<Segment> segments, int iPosition)
    {
      int segCount = segments.Count;
      if (iPosition > segCount - 1)
        return;

      if (iPosition <= 0)
        return;

      int iRepeat = segCount - iPosition;

      List<Segment> newSegs = new();
      for (int j = 0; j < iRepeat; j++)
      {
        newSegs.Clear();
        var lastSeg = segments[^1];
        newSegs.Add(lastSeg);
        for (int i = 0; i < segments.Count - 1; i++)
          newSegs.Add(segments[i]);

        //update segments to newsegs
        //segments = newSegs;
        segments.Clear();
        for (int i = 0; i < newSegs.Count; i++)
          segments.Add(newSegs[i]);
      }
    }

    internal static int FindLongestSegment(List<Segment> segments)
    {
      double longSegment = 0.0; //start at zero length
      int Index = 0;
      int cnt = 0;
      foreach (Segment seg in segments)
      {
        var length1 = ChordDistance(seg);
        longSegment = length1 > longSegment ? length1 : longSegment;
        if (longSegment == length1)
          Index = cnt;
        cnt++;
      }
      return Index;
    }


    internal static bool SimplifyPolygonByLastAndFirstSegmentTangency(ref Geometry polygon, out List<Segment> segments)
    {
      segments = new();
      //check if the closing last segment of the polygon is tangent to the first segment. If it is then replace
      //the last segment with a new merged segment
      double _metersPerUnitDataset = 1.0;
      bool bHasChanges = false;
      var sr = polygon.SpatialReference;
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      if (sr.IsProjected)
      {
        xyTol = sr.XYTolerance;
        _metersPerUnitDataset = sr.Unit.ConversionFactor;
      }

      switch (polygon.GeometryType)
      {
        case GeometryType.Point:
          break;
        case GeometryType.Polyline:
          break;
        case GeometryType.Polygon:
          {
            var parts = ((Multipart)polygon).Parts;
            foreach (var ringSegments in parts)
            {
              int iSegCount = ringSegments.Count;
              var lstLineSegments = ringSegments.ToList();

              var longestSeg = 0.0;
              foreach (Segment segment in lstLineSegments)
                longestSeg = (segment.Length > longestSeg) ? segment.Length : longestSeg;

              var firstSeg = lstLineSegments[0];
              var lastSeg = lstLineSegments[^1];

              var envHalfDiagLength =
                Math.Sqrt(Math.Pow(polygon.Extent.Width, 2.0) + Math.Pow(polygon.Extent.Height, 2.0)) / 2.0 * _metersPerUnitDataset;

              var pSeg1 = firstSeg;
              var pSeg0 = lastSeg;

              //if a segment length is 0 then skip
              if (ChordDistance(pSeg1) < xyTol * 1.5)
                return false;
              if (ChordDistance(pSeg0) < xyTol * 1.5)
                return false;
              //test that the segments are connected within XY tolerance
              //and confirm that segments run head-to-toe.
              var pt01 = pSeg0.EndCoordinate;
              var pt02 = pSeg1.StartCoordinate;
              var dist = Math.Sqrt(Math.Pow(pt01.X - pt02.X, 2.0) + Math.Pow(pt01.Y - pt02.Y, 2.0));
              if (dist > xyTol * 1.5)
                return false;

              //test that segments are not collapsed to the same point (side case for closed loop polylines)
              var pt03 = pSeg0.StartPoint;
              var pt04 = pSeg1.EndPoint;
              dist = Math.Sqrt(Math.Pow(pt03.X - pt04.X, 2.0) + Math.Pow(pt03.Y - pt04.Y, 2.0));
              if (dist < xyTol * 1.5)
                return false;

              bool Is2CircularArcs;
              bool Is2StraightLines;
              if (pSeg0 is EllipticArcSegment && pSeg1 is EllipticArcSegment)
              {
                Is2CircularArcs = true;
                Is2StraightLines = false;
                var pCirc0 = pSeg0 as EllipticArcSegment;
                var pCirc1 = pSeg1 as EllipticArcSegment;
                if (!Module1.IsShortFlatCircularArcSegment(pCirc0, 50.0, xyTol) &&
                  !Module1.IsShortFlatCircularArcSegment(pCirc1, 50.0, xyTol))
                {
                  if (pCirc0.IsCounterClockwise && !pCirc1.IsCounterClockwise)
                    return false;

                  if (!pCirc0.IsCounterClockwise && pCirc1.IsCounterClockwise)
                    return false;
                }
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
                Module1.IsSegmentPairTangent(pSeg0, pSeg1, MaxAllowedOffsetFromUserInMeters: 0.2, MinOffsetToleranceInMeters: 0.02,
                    MaxFeatureLengthInMeters: envHalfDiagLength, OffsetRatio: 250.00);

              if (segmentsAreTangent && Is2CircularArcs)
              {
                if (!Module1.HasSameCenterPoint(pSeg0, pSeg1) &&
                  !Module1.IsShortFlatCircularArcSegment(pSeg0 as EllipticArcSegment, 50.0, xyTol) &&
                  !Module1.IsShortFlatCircularArcSegment(pSeg1 as EllipticArcSegment, 50.0, xyTol))
                  return false;

                var arcOr = ((EllipticArcSegment)pSeg0).IsCounterClockwise ?
                  ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;

                //Detect true elliptical arcs
                var trueEllipticalArcs = !(((EllipticArcSegment)pSeg0).IsCircular &&
                 ((EllipticArcSegment)pSeg1).IsCircular);

                var arcMinMaj =
                   Math.Abs(((EllipticArcSegment)pSeg1).CentralAngle) + Math.Abs(((EllipticArcSegment)pSeg0).CentralAngle)
                   < Math.PI ? MinorOrMajor.Minor : MinorOrMajor.Major;

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
                  longerSeg.SemiMajorAxis, longerSeg.MinorMajorRatio, longerSeg.RotationAngle, arcMinMaj, arcOr);

                //Replace first and last segments with single merged segment
                lstLineSegments.RemoveRange(iSegCount - 1, 1); //remove last segment
                lstLineSegments.RemoveRange(0, 1); //remove first segment
                lstLineSegments.Add(pMergedCircularOrEllipticArc); //add the merged segment on the end
                bHasChanges = true;
              }

              if (segmentsAreTangent && Is2StraightLines)
              {
                var pMergedLine = LineBuilderEx.CreateLineSegment(pSeg0.StartPoint, pSeg1.EndPoint);
                //Replace first and last segments with one
                lstLineSegments.RemoveRange(iSegCount - 1, 1); //remove last segment
                lstLineSegments.RemoveRange(0, 1); //remove first segment
                lstLineSegments.Add(pMergedLine); //add the merged segment on the end
                bHasChanges = true;
              }
              try
              {
                if (bHasChanges)
                {
                  var updatedPolygon = PolygonBuilderEx.CreatePolygon(lstLineSegments);
                  if (!GeometryEngine.Instance.IsSimpleAsFeature(updatedPolygon))
                    updatedPolygon = GeometryEngine.Instance.SimplifyAsFeature(updatedPolygon) as Polygon;
                  polygon = updatedPolygon;
                  segments = lstLineSegments;
                  return true;
                }
              }
              catch
              {//what cases cause CreatePolygon to fail?
                return false;
              }
            }
          }
          break;
        default:
          ;// Unsupported geometry type.
          break;
      }
      return false;

    }

    internal static bool HasSegmentsSharingAVertex2(ReadOnlySegmentCollection segments)
    {
      //test for special case where segments share common vertex, "pretzel"
      var xyTol = 0.001 / 6371000.0 * Math.PI / 180.0; //default to 1mm in GCS decimal degree
      double _metersPerUnitDataset = 1.0;

      string sDecPrecFmt = "F9";

      if (segments.SpatialReference.IsProjected)
      {
        xyTol = segments.SpatialReference.XYTolerance;
        _metersPerUnitDataset = segments.SpatialReference.Unit.ConversionFactor;
        sDecPrecFmt = "F3";
      }

      Module1.GetPolylineFromSegments(segments, out Polyline polylineGeometry);
      if (polylineGeometry == null)
        return false;

      ReadOnlyPointCollection ptColl = polylineGeometry.Points;

      List<string> lst = new();
      foreach (var pt in ptColl)
        lst.Add(pt.X.ToString(sDecPrecFmt) + "," + pt.Y.ToString(sDecPrecFmt));

      lst.RemoveAt(lst.Count - 1);
      lst.RemoveAt(0);

      var query = lst.GroupBy(x => x)
              .Where(g => g.Count() > 1)
              .Select(y => y.Key)
              .ToList();

      return query.Count > 0;
    }

    internal static void GetPolylineFromSegments(ReadOnlySegmentCollection segments, out Polyline polyline)
    {
      polyline = null;
      try
      {
        // Create a PolylineBuilder to construct a polyline geometry
        PolylineBuilderEx polylineBuilder = new(segments.SpatialReference);
        // Iterate through each segment in the collection adding start and end points to the polyline builder
        foreach (Segment segment in segments)
          polylineBuilder.AddSegment(segment);
        // Build the polyline geometry using the polyline builder
        polyline = polylineBuilder.ToGeometry();
      }
      catch
      {
        return;
      }
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

    internal static void GetVisibleFeatureLayers(MapView mapView, 
      out List<FeatureLayer> featureVisibleLayers, 
      bool IncludePointLayers = true, bool IncludeLineLayers = true, bool IncludePolygonLayers = true, 
      bool IgnoreTopologyLayers = true)
    {
      List<FeatureLayer> featureLayers = 
        mapView.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();
      
      featureVisibleLayers = new();
      try
      {
        foreach (var lyr in featureLayers)
        {
          if (IgnoreTopologyLayers)
          {
            if (IsTopologyErrorLayer(lyr))
              continue;
            if (IsTopologyDirtyAreaLayer(lyr))
              continue;
          }

          var fc = lyr.GetFeatureClass();
          if (fc == null)
            continue;

          GeometryType geomType = fc.GetDefinition().GetShapeType();

          if (!featureVisibleLayers.Contains(lyr))
          {
            if (lyr.IsVisibleInView(mapView))
            {
              if (geomType == GeometryType.Point && IncludePointLayers)
                featureVisibleLayers.Add(lyr);
              if (geomType == GeometryType.Polyline && IncludeLineLayers)
                featureVisibleLayers.Add(lyr);
              if (geomType == GeometryType.Polygon && IncludePolygonLayers)
                featureVisibleLayers.Add(lyr);
            }
          }
        }
      }
      catch
      {;}
    }

    internal static bool GetCOGOLineFeatureLayersSelection(MapView myActiveMapView,
      out Dictionary<FeatureLayer, List<long>> COGOLineSelections)
    {
      List<FeatureLayer> featureLayer = new();
      COGOLineSelections = new();

      try
      {
        var fLyrList = myActiveMapView?.Map?.GetLayersAsFlattenedList()?.OfType<FeatureLayer>()?.
          Where(l => l != null).Where(l => (l as Layer).ConnectionStatus != ConnectionStatus.Broken).
          Where(l => l.GetFeatureClass().GetDefinition().IsCOGOEnabled());

        if (fLyrList == null) return false;

        foreach (var fLyr in fLyrList)
        {
          if (fLyr.SelectionCount > 0)
            featureLayer.Add(fLyr);
        }
      }
      catch
      { return false; }
      foreach (var lyr in featureLayer)
      {
        var fc = lyr.GetFeatureClass();
        List<long> lstOids = new();
        using (RowCursor rowCursor = lyr.GetSelection().Search())
        {
          while (rowCursor.MoveNext())
          {
            using (Row rowFeat = rowCursor.Current)
            {
              if (!COGOLineSelections.ContainsKey(lyr))
                COGOLineSelections.Add(lyr, lstOids);
              lstOids.Add(rowFeat.GetObjectID());
            }
          }
        }
        if (lstOids.Count > 0)
          COGOLineSelections[lyr] = lstOids;
      }
      return true;
    }

    internal static void GetFeatureLayerSelections(List<FeatureLayer> featureLayers,
      out Dictionary<FeatureLayer, List<long>> featureLayerSelections, bool IgnoreTopologyLayers = true)
    {
      featureLayerSelections = new();
      foreach (var lyr in featureLayers)
      {
        if (IgnoreTopologyLayers)
        {
          if (IsTopologyErrorLayer(lyr))
            continue;
          if (IsTopologyDirtyAreaLayer(lyr))
            continue;
        }

        List<long> lstOids = new();
        using (RowCursor rowCursor = lyr.GetSelection().Search())
        {
          while (rowCursor.MoveNext())
          {
            using Row rowFeat = rowCursor.Current;
            if (!featureLayerSelections.ContainsKey(lyr))
              featureLayerSelections.Add(lyr, lstOids);
            lstOids.Add(rowFeat.GetObjectID());
          }
        }
        if (lstOids.Count > 0)
          featureLayerSelections[lyr] = lstOids;
      }
    }

    internal static bool IsTopologyErrorLayer(FeatureLayer featureLayer)
    {
      List<string> lstTopoFields = 
        new() { "originclassid", "originid", "destclassid","destid","toporuletype", "toporuleid","isexception" };
      var lst = featureLayer.GetFieldDescriptions();
      foreach (var fldDesc in lst)
      {
        if (lstTopoFields.Contains(fldDesc.Name.ToLower()))
          lstTopoFields.Remove(fldDesc.Name.ToLower());
      }

      return lstTopoFields.Count==0; 
    }

    internal static bool IsTopologyDirtyAreaLayer(FeatureLayer featureLayer)
    {
      List<string> lstTopoFields =
        new() { "dirtyarea", "isretired"};
      var lst = featureLayer.GetFieldDescriptions();
      foreach (var fldDesc in lst)
      {
        if (lstTopoFields.Contains(fldDesc.Name.ToLower()))
          lstTopoFields.Remove(fldDesc.Name.ToLower());
      }
      return lstTopoFields.Count == 0;
    }

    internal static double InverseDirectionAsNorthAzimuth(Coordinate2D FromCoordinate, Coordinate2D ToCoordinate, bool Reversed)
    {
      var DirectionInPolarRadians = LineBuilderEx.CreateLineSegment(FromCoordinate, ToCoordinate).Angle;
      if (Reversed)
        DirectionInPolarRadians += Math.PI;
      return PolarRadiansToNorthAzimuthDecimalDegrees(DirectionInPolarRadians);
    }

    internal static double PolarRadiansToNorthAzimuthDecimalDegrees(double InPolarRadians)
    {
      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = ArcGIS.Core.SystemCore.DirectionType.Polar,
        DirectionUnitsIn = ArcGIS.Core.SystemCore.DirectionUnits.Radians,
        DirectionTypeOut = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth,
        DirectionUnitsOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees
      };
      return AngConv.ConvertToDouble(InPolarRadians, ConvDef);
    }

    internal static double NorthAzimuthDecimalDegreesToPolarRadians(double InDecimalDegrees)
    {
      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth,
        DirectionUnitsIn = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees,
        DirectionTypeOut = ArcGIS.Core.SystemCore.DirectionType.Polar,
        DirectionUnitsOut = ArcGIS.Core.SystemCore.DirectionUnits.Radians
      };
      return AngConv.ConvertToDouble(InDecimalDegrees, ConvDef);
    }
    internal static double DistanceInGCS(Coordinate2D point1LatLonDecDeg, Coordinate2D point2LatLonDecDeg)
    {
      var Lat1 = point1LatLonDecDeg.Y; var Lon1 = point1LatLonDecDeg.X;
      var Lat2 = point2LatLonDecDeg.Y; var Lon2 = point2LatLonDecDeg.X;

      var R = 6371000;//meters

      var φ1 = Lat1 * Math.PI / 180; var φ2 = Lat2 * Math.PI / 180;
      var Δφ = (Lat2 - Lat1) * Math.PI / 180; var Δλ = (Lon2 - Lon1) * Math.PI / 180;

      var a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) + Math.Cos(φ1) * Math.Cos(φ2) * Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
      var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
      var d = R * c;
      return d;
    }

  }
}
