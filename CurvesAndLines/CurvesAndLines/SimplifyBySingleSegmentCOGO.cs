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
  internal class SimplifyBySingleSegmentCOGO : Button
  {
    protected async override void OnClick()
    {
      CancelableProgressorSource cps = new("Simplify by COGO Segment", "Canceled");
      Dictionary<FeatureLayer, List<long>> cogoLineLayer2ReportIds = new();
      
      int maxReportedLines = 11;
      string sReportResult = "";
      List<string> sReportChangedLineList = new();
      List<string> sReportDisjointPointList = new();
      string errorMessage = await QueuedTask.Run( () =>
      {
        int ignoreCount = 0;
        int inconsistentRadiusArcLengthChordCount = 0;
        int missingCOGOAttributesCount = 0;
        int geometryCOGOComparisonFail = 0;
        int iAllSelectedLinesProcessedCount = 0;
        int updatedLineGeometryCount = 0;
        int geometryUnchangedCount = 0;

        try
        {
          var map = MapView.Active.Map;
          var mapSR = map.SpatialReference;

          bool mapInGCS = false;

          double mapMetersPerUnit = 1.0;
          if (mapSR.IsProjected)
            mapMetersPerUnit = mapSR.Unit.ConversionFactor;
          else
            mapInGCS = true;

          if (!Module1.GetCOGOLineFeatureLayersSelection(MapView.Active,
            out Dictionary<FeatureLayer, List<long>> cogoLineLayerIds))
            return "Error getting COGO layer selections.";

          var editOper = new EditOperation()
          {
            Name = "Simplify To Single Segment",
            ShowModalMessageAfterFailure = true,
            ShowProgressor = true,
            SelectModifiedFeatures = false
          };
          QueryFilter queryFilterSelectedLines = new();

          foreach (var cogoLyr in cogoLineLayerIds)
          {
            var cogoLineFC = cogoLyr.Key.GetFeatureClass();
            var fcDefinition = cogoLineFC.GetDefinition();
            var datasetProjection = fcDefinition.GetSpatialReference();
            var chordComparisonTolerance = datasetProjection.XYTolerance * 100;
            var chordComparisonToleranceGCS = datasetProjection.XYTolerance * 100 * Math.PI / 180.0 * 6378100.0;

            queryFilterSelectedLines.ObjectIDs = cogoLyr.Value;

            //check for field presence
            bool hasCOGOTypeFld = fcDefinition.FindField("cogotype") > -1;
            bool hasAzimuthTypeFld = fcDefinition.FindField("azimuthtype") > -1;
            bool hasScaleFactorFld = fcDefinition.FindField("scale") > -1;

            bool datasetInGCS = false;
            double datasetMetersPerUnit = 1.0;
            if (fcDefinition.GetSpatialReference().IsProjected)
              datasetMetersPerUnit = fcDefinition.GetSpatialReference().Unit.ConversionFactor;
            else
              datasetInGCS = true;

            if (mapInGCS && datasetInGCS)
              return "Please ensure that the map or dataset have a projected coordinate system.";

            cps.Progressor.Message = "Updating " + cogoLyr.Key.ToString();

            List<long> lstReportOids = new();

            using (RowCursor rowCursor = cogoLyr.Key.Search(queryFilterSelectedLines))
            {
              while (rowCursor.MoveNext())
              {
                using (Row rowLine = rowCursor.Current)
                {
                  var lineGeom = (rowLine as Feature).GetShape();
                  iAllSelectedLinesProcessedCount++;
                  if (lineGeom is not Polyline)
                  {
                    ignoreCount++;
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  //check for multi-parts and skip (not implemented)
                  if ((lineGeom as Polyline).PartCount > 1)
                  {
                    ignoreCount++;
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  var oid = rowLine.GetObjectID();
                  var cogoDir = rowLine["direction"];
                  var cogoDist = rowLine["distance"];
                  var cogoRadius = rowLine["radius"];
                  var cogoArclength = rowLine["arclength"];
                  double scaleFactor = 1.0;
                  if (hasScaleFactorFld)
                  {
                    var cogoScale = rowLine["scale"];
                    if (cogoScale != null)
                      scaleFactor = (double)cogoScale;
                    if (scaleFactor <= 0.0)
                      scaleFactor = 1.0;
                  }

                  bool hasNullDistArcLengthAndRadius =
                    cogoDist == null && cogoArclength == null
                      && cogoRadius == null;

                  bool hasDirection = cogoDir != null;

                  bool isFullCOGOStraightLine = cogoRadius == null &&
                    cogoArclength == null && cogoDir != null && cogoDist != null;
                  bool isDistanceOnlyCOGOStraightLine = cogoRadius == null && 
                    cogoArclength == null && cogoDir == null && cogoDist != null;
                  bool isDirectionOnlyCOGOStraightLine = cogoRadius == null &&
                    cogoArclength == null && cogoDir != null && cogoDist == null;

                  bool isFullCOGOCircularArc = cogoRadius != null && cogoArclength != null;
                  bool isArcLengthOnlyCOGOCircularArc = cogoRadius == null && cogoArclength != null;
                  bool isRadiusOnlyCOGOCircularArc = cogoRadius != null && cogoArclength == null;
                  bool isConflictingCOGOCircularArc = 
                    (cogoRadius != null && cogoDist != null) || (cogoArclength != null && cogoDist != null);

                  if (isConflictingCOGOCircularArc)
                  {
                    inconsistentRadiusArcLengthChordCount++;
                    ignoreCount++;
                    cps.Progressor.Value += 1;
                    continue; 
                  }

                  if (hasNullDistArcLengthAndRadius && !hasDirection)
                  {
                    missingCOGOAttributesCount++;
                    ignoreCount++;
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  Segment newSegment = null;

                  //Special case: check for spiral
                  var r2 = rowLine["radius2"];
                  bool specialCaseSpiral = r2 != null;
                  bool bReversedGeometry = false;

                  ICollection<Segment> LineSegments = new List<Segment>();
                  (lineGeom as Polyline).GetAllSegments(ref LineSegments);
                  int numSegments = LineSegments.Count;
                  bool bOriginalIsSingleSegment = numSegments == 1;

                  IList<Segment> iList = LineSegments as IList<Segment>;
                  var originalPolyLineLength = (lineGeom as Polyline).Length;
                  var originalMidPoint = GeometryEngine.Instance.MovePointAlongLine(lineGeom as Multipart, 0.5, true, 0.0, SegmentExtensionType.NoExtension);

                  //create a new segment from start point to end point
                  Segment FirstSeg = iList[0];
                  Segment LastSeg = iList[numSegments - 1];
                  newSegment = LineBuilderEx.CreateLineSegment(FirstSeg.StartCoordinate, LastSeg.EndCoordinate, datasetProjection);

                  //if start point and end point are the same then bail (closed loop polyline)
                  var startPointDS = MapPointBuilderEx.CreateMapPoint(FirstSeg.StartCoordinate, datasetProjection);
                  var endPointDS = MapPointBuilderEx.CreateMapPoint(LastSeg.EndCoordinate, datasetProjection);

                  var dist = GeometryEngine.Instance.GeodesicDistance(startPointDS, endPointDS);
                  if (dist < chordComparisonTolerance)
                  {
                    geometryCOGOComparisonFail++;
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  if (!specialCaseSpiral)
                  {
                    //get geometry chord distance and direction
                    double geomDirectionInNorthAzimuthDegrees = Module1.InverseDirectionAsNorthAzimuth(newSegment.StartCoordinate,
                      newSegment.EndCoordinate, false);

                    var geometryDirectionInPolarRadians = Module1.NorthAzimuthDecimalDegreesToPolarRadians(geomDirectionInNorthAzimuthDegrees);
                    var geometryChordDistance = newSegment.Length;
                    var geometryGCSChordDistance = newSegment.Length;

                    if (datasetInGCS)
                    {
                      var geom = PolylineBuilderEx.CreatePolyline(newSegment);
                      geometryChordDistance = GeometryEngine.Instance.GeodesicLength(geom);
                    }

                    var halfCircleCircumference = 0.5 * Math.PI * geometryChordDistance; //use a half-circle as a tolerance for straight line COGO

                    if (cogoDist != null) //unit conversion/projection
                    {
                      var COGODistance = (double)cogoDist;
                      if (COGODistance > halfCircleCircumference && !isFullCOGOCircularArc)
                      {//too much difference between COGO distance and geometry length
                        ignoreCount++;
                        geometryCOGOComparisonFail++;
                        cps.Progressor.Value += 1;
                        continue;
                      }
                    }

                    if (hasDirection)
                    { //check if the geometry needs to be reversed
                      double COGODirection = (double)cogoDir;
                      var t = Math.Abs(geomDirectionInNorthAzimuthDegrees - COGODirection) % 360.0; //fMOD in C++
                      var delta = 180.0 - Math.Abs(t - 180.0);
                      //if geometry and entered values are different by more than a whole 90° quadrant:
                      bReversedGeometry = delta >= 90.0;
                    }

                    if (bReversedGeometry & !isFullCOGOCircularArc)
                      newSegment = LineBuilderEx.CreateLineSegment(LastSeg.EndCoordinate, FirstSeg.StartCoordinate, datasetProjection);

                    if (isFullCOGOCircularArc)
                    {//Since the end points are not allowed to move, the circular arc can be regenerated from either
                     //Radius or Arclength COGO attributes.
                     //The radius is used also to define curves to left / right, and arclength is used for major / minor
                      double COGORadius = Math.Abs((double)cogoRadius) * scaleFactor;
                      double COGOArclength = Math.Abs((double)cogoArclength) * scaleFactor;

                      if (COGOArclength > COGORadius * 2.0 * Math.PI)
                      {
                        inconsistentRadiusArcLengthChordCount++;
                        cps.Progressor.Value += 1;
                        continue;
                      }

                      ArcOrientation orientation = (double)cogoRadius < 0.0 ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
                      //compute the geometry from cogo
                      var circArcFromCOGO = EllipticArcBuilderEx.CreateCircularArc(newSegment, true, orientation, COGORadius, COGOArclength, datasetProjection);
                      double delta = circArcFromCOGO.CentralAngle;
                      MinorOrMajor minMaj = Math.Abs(delta) > Math.PI ? MinorOrMajor.Major : MinorOrMajor.Minor;

                      double computedCOGOChordDistance = LineBuilderEx.CreateLineSegment(circArcFromCOGO.StartPoint, circArcFromCOGO.EndPoint, datasetProjection).Length;
                      double testDifference = Math.Abs(computedCOGOChordDistance - geometryChordDistance);
                      if (datasetInGCS)
                        chordComparisonTolerance = chordComparisonToleranceGCS;

                      bool isHalfCircle = Math.Abs(COGORadius * Math.PI - COGOArclength) <= chordComparisonTolerance;
                      if (testDifference < chordComparisonTolerance) //tolerance unit conversion checks for different maps
                      { //there is still enough to compute a curve
                        inconsistentRadiusArcLengthChordCount++;// arclength and radius are inconsistent with point locations
                                                                // update the circular arc using COGO radius and chord distance
                        var newDeltaInRadians = Math.PI;
                        if (!isHalfCircle)
                          newDeltaInRadians = 2.0 * Math.Asin(computedCOGOChordDistance / (2.0 * COGORadius));

                        newDeltaInRadians = minMaj == MinorOrMajor.Minor ? newDeltaInRadians : (2.0 * Math.PI) - newDeltaInRadians;
                        COGOArclength = newDeltaInRadians * COGORadius; //updated ArcLength to match radius and chord length
                      }
                      else //if (Math.Abs(computedCOGOChordDistance - geometryChordDistance) < 0.1)
                      {//the COGO computed chord length is too short to form a valid circular arc between the existing end points
                        inconsistentRadiusArcLengthChordCount++;
                        cps.Progressor.Value += 1;
                        continue;
                      }

                      if (datasetInGCS)
                      {
                        //delta from chord and radius
                        COGORadius = orientation == ArcOrientation.ArcClockwise ? COGORadius : -COGORadius;
                        var halfDelta = Math.Asin(geometryChordDistance / (2.0 * COGORadius));

                        //Consider Major circular arcs
                        if (minMaj == MinorOrMajor.Major)
                          halfDelta = Math.PI - halfDelta;

                        COGORadius = Math.Abs(COGORadius);

                        //make the tangent segment
                        var endPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(newSegment.StartPoint, 
                            geometryDirectionInPolarRadians + Math.PI, geometryGCSChordDistance, datasetProjection);

                        var tangentSegBuilder = SegmentBuilderEx.ConstructSegmentBuilder(newSegment);
                        tangentSegBuilder.StartPoint = endPoint; //180°
                        tangentSegBuilder.EndPoint = newSegment.StartPoint; //new end point is chord start point
                        var tangentSeg = tangentSegBuilder.ToSegment();

                        //project newSegment(the chord) to Map
                        var gcsDSLine = PolylineBuilderEx.CreatePolyline(tangentSeg, datasetProjection);
                        Polyline newLineMapSR = GeometryEngine.Instance.Project(gcsDSLine, mapSR) as Polyline;

                        //correct for halfDelta
                        var anchorPoint = newLineMapSR.Points[1];
                        newLineMapSR = (Polyline)GeometryEngine.Instance.Rotate(newLineMapSR, anchorPoint, halfDelta);

                        ICollection<Segment> mapSRLineSegments = new List<Segment>();
                        newLineMapSR.GetAllSegments(ref mapSRLineSegments);
                        IList<Segment> imapList = mapSRLineSegments as IList<Segment>;
                        var newSegmentMapSR = imapList[0];
                        Segment circArcFromCOGOGCS = null;
                        try
                        {
                          circArcFromCOGOGCS = EllipticArcBuilderEx.CreateCircularArc(newSegmentMapSR, false, orientation, COGORadius/mapMetersPerUnit, 
                            COGOArclength/mapMetersPerUnit, mapSR);
                        }
                        catch
                        {
                          inconsistentRadiusArcLengthChordCount++;
                          cps.Progressor.Value += 1;
                          continue; 
                        }
                        
                        var mapCirc = PolylineBuilderEx.CreatePolyline(circArcFromCOGOGCS,mapSR);
                        //before projection test for special case half-circle arcs that failed creation
                        var qaCA = (circArcFromCOGOGCS as EllipticArcSegment).CentralAngle;
                        var qaEndAng = (circArcFromCOGOGCS as EllipticArcSegment).EndAngle;
                        var qaRotationAngle = (circArcFromCOGOGCS as EllipticArcSegment).RotationAngle;
                        var qaStartAng = (circArcFromCOGOGCS as EllipticArcSegment).StartAngle;
                        if (Double.IsNaN(qaCA) || Double.IsNaN(qaEndAng) || 
                            Double.IsNaN(qaRotationAngle) || Double.IsNaN(qaStartAng))
                        {
                          inconsistentRadiusArcLengthChordCount++;
                          cps.Progressor.Value += 1;
                          continue;
                        }
                        //

                        var prjMapCirc = GeometryEngine.Instance.Project(mapCirc, datasetProjection);
                        ICollection<Segment> prjMapLineSegments = new List<Segment>();
                        (prjMapCirc as Polyline).GetAllSegments(ref prjMapLineSegments);
                        IList<Segment> iprjMapCircList = prjMapLineSegments as IList<Segment>;

                        newSegment = iprjMapCircList[0];
                        //do another check on the computed end point versus the original end point. GCS
                        //if they're within 5 cm force close - snapping
                        var distCheck = GeometryEngine.Instance.GeodesicDistance(newSegment.EndPoint, endPointDS);
                        if (distCheck < chordComparisonTolerance / 2.0 * Math.Sqrt(2.0)) //&& distCheck > chordComparisonTolerance / 75.0
                        { 
                          var ctrPoint = (newSegment as EllipticArcSegment).CenterPoint;
                          newSegment = EllipticArcBuilderEx.CreateCircularArc(newSegment.StartPoint, endPointDS, ctrPoint, orientation);
                        }
                      }
                      if (bReversedGeometry)
                      {
                        try
                        {
                          if (!datasetInGCS)
                          {
                            if (!isHalfCircle)
                            {
                              newSegment = EllipticArcBuilderEx.CreateCircularArc(FirstSeg.EndCoordinate.ToMapPoint(), geometryChordDistance,
                                geometryDirectionInPolarRadians + Math.PI, COGOArclength, orientation, datasetProjection);
                            }
                            else
                            {//make a half circle from COGORadius and geometryChordDistance/geometryDirectionInPolarRadians
                              //find the center of circular arc
                              var ctrPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(FirstSeg.StartCoordinate.ToMapPoint(),
                                geometryDirectionInPolarRadians, geometryChordDistance / 2.0);
                              // calculate the interior point on the correct side using sign of cogo radius attribute
                              var directionPlusMinus90 =
                                orientation == ArcOrientation.ArcCounterClockwise ? geometryDirectionInPolarRadians + Math.PI / 2.0 : geometryDirectionInPolarRadians - Math.PI / 2.0;

                              MapPoint qtrPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(ctrPoint, directionPlusMinus90, COGORadius);
                              newSegment = EllipticArcBuilderEx.CreateCircularArc(LastSeg.EndCoordinate.ToMapPoint(),
                                FirstSeg.StartCoordinate.ToMapPoint(), qtrPoint.Coordinate2D);
                            }
                          }
                          else
                          {
                            //recompute the circular arc for reversed geometry case
                            var revChord = LineBuilderEx.CreateLineSegment(newSegment.EndPoint, newSegment.StartPoint);
                            newSegment = EllipticArcBuilderEx.CreateCircularArc(revChord.StartPoint, revChord.Length, revChord.Angle,
                              (newSegment as EllipticArcSegment).Length, orientation);
                          }
                        }
                        catch
                        {
                          inconsistentRadiusArcLengthChordCount++;
                          cps.Progressor.Value += 1;
                          continue;
                        }
                      }
                      else
                      {
                        try
                        {
                          if (!datasetInGCS)
                          {
                            if (!isHalfCircle)
                            {
                              newSegment = EllipticArcBuilderEx.CreateCircularArc(FirstSeg.StartCoordinate.ToMapPoint(), geometryChordDistance,
                                geometryDirectionInPolarRadians, COGOArclength, orientation, datasetProjection);
                            }
                            else
                            {//make a half circle from COGORadius and geometryChordDistance/geometryDirectionInPolarRadians
                              //find the center of circular arc
                              var ctrPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(FirstSeg.StartCoordinate.ToMapPoint(),
                                geometryDirectionInPolarRadians, geometryChordDistance / 2.0);
                              // calculate the interior point on the correct side using sign of cogo radius attribute
                              var directionPlusMinus90 =
                                orientation == ArcOrientation.ArcClockwise ? geometryDirectionInPolarRadians + Math.PI / 2.0 : geometryDirectionInPolarRadians - Math.PI / 2.0;

                              MapPoint qtrPoint = GeometryEngine.Instance.ConstructPointFromAngleDistance(ctrPoint, directionPlusMinus90, COGORadius);
                              newSegment = EllipticArcBuilderEx.CreateCircularArc(FirstSeg.StartCoordinate.ToMapPoint(),
                                LastSeg.EndCoordinate.ToMapPoint(), qtrPoint.Coordinate2D);
                            }
                          }
                        }
                        catch
                        {
                          inconsistentRadiusArcLengthChordCount++;
                          cps.Progressor.Value += 1;
                          continue;
                        }
                      }
                    }

                    if (isArcLengthOnlyCOGOCircularArc)
                    {
                      missingCOGOAttributesCount++;
                      cps.Progressor.Value += 1;
                      continue;
                    }

                    if (isRadiusOnlyCOGOCircularArc)
                    {
                      double COGORadius = Math.Abs((double)cogoRadius) * scaleFactor; 
                      var newDeltaInRadians = 2.0 * Math.Asin(geometryChordDistance / (2.0 * COGORadius));
                      if (double.IsNaN(newDeltaInRadians))
                      {
                        inconsistentRadiusArcLengthChordCount++;
                        cps.Progressor.Value += 1;
                        continue;
                      }
                      double COGOArclength = newDeltaInRadians * COGORadius; //computed ArcLength for COGO radius and chord length
                      ArcOrientation orientation = (double)cogoRadius < 0.0 ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;

                      try
                      {
                        if (bReversedGeometry)
                        {
                          if (!datasetInGCS)
                            newSegment = EllipticArcBuilderEx.CreateCircularArc(FirstSeg.EndCoordinate.ToMapPoint(), geometryChordDistance,
                              geometryDirectionInPolarRadians + Math.PI, COGOArclength, orientation, datasetProjection);
                          else
                          {//TODO: currently only implemented for GCS when both radius and arclength attributes are present
                            inconsistentRadiusArcLengthChordCount++;
                            cps.Progressor.Value += 1;
                            continue;
                          }
                        }
                        else
                        {

                          if (!datasetInGCS)
                            newSegment = EllipticArcBuilderEx.CreateCircularArc(FirstSeg.StartCoordinate.ToMapPoint(), geometryChordDistance,
                              geometryDirectionInPolarRadians, COGOArclength, orientation, datasetProjection);
                          else
                          { //TODO: currently only implemented for GCS when both radius and arclength attributes are present
                            inconsistentRadiusArcLengthChordCount++;
                            cps.Progressor.Value += 1;
                            continue;
                          }
                        }
                      }
                      catch
                      {
                        inconsistentRadiusArcLengthChordCount++;
                        cps.Progressor.Value += 1;
                        continue;
                      }

                    }
                  }
                  else
                  {//special case spiral, ignore
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  if (newSegment == null)
                  {
                    ignoreCount++;
                    cps.Progressor.Value += 1;
                    continue;
                  }

                  var newGeom = PolylineBuilderEx.CreatePolyline(newSegment, datasetProjection);
                  cps.Progressor.Value += 1;

                  //quality checks before writing
                  ICollection<Segment> datasetQASegments = new List<Segment>();
                  (newGeom as Polyline).GetAllSegments(ref datasetQASegments);
                  IList<Segment> idatasetQASegmentList = datasetQASegments as IList<Segment>;
                  
                  var QAstartPoint = idatasetQASegmentList[0].StartPoint;
                  var QAendPoint = idatasetQASegmentList[idatasetQASegmentList.Count-1].EndPoint;
                  var QAmidPoint = GeometryEngine.Instance.MovePointAlongLine(newGeom, 0.5, true, 0.0, SegmentExtensionType.NoExtension);

                  if (bReversedGeometry)
                  {
                    QAstartPoint = idatasetQASegmentList[idatasetQASegmentList.Count - 1].EndPoint;
                    QAendPoint = idatasetQASegmentList[0].StartPoint;
                  }

                  if (GeometryEngine.Instance.Disjoint(QAstartPoint, startPointDS))
                  {//don't allow an edit with disjoint start or end points
                    var distQA = GeometryEngine.Instance.GeodesicDistance(QAstartPoint, startPointDS);
                    sReportDisjointPointList.Add("Disjoint start point (" + distQA.ToString("F3") + 
                      " m). Result ignored, oid: " + oid.ToString());
 
                    if (!cogoLineLayer2ReportIds.ContainsKey(cogoLyr.Key))
                      cogoLineLayer2ReportIds.Add(cogoLyr.Key, lstReportOids);
                    lstReportOids.Add(oid);

                    continue;
                  }

                  if (GeometryEngine.Instance.Disjoint(QAendPoint, endPointDS))
                  {//don't allow disjoint start or end points
                    var distQA = GeometryEngine.Instance.GeodesicDistance(QAendPoint, endPointDS);
                    sReportDisjointPointList.Add("Disjoint end point (" + distQA.ToString("F3") + 
                      " m). Result ignored, oid: " + oid.ToString());
                    
                    if (!cogoLineLayer2ReportIds.ContainsKey(cogoLyr.Key))
                      cogoLineLayer2ReportIds.Add(cogoLyr.Key, lstReportOids);
                    lstReportOids.Add(oid);

                    continue;
                  }

                  var qaLengthTest = 10.0 * chordComparisonTolerance;
                  if (datasetInGCS)
                    qaLengthTest = 10.0 * chordComparisonTolerance / 6378100.0 * 180.0 / Math.PI;

                  var qaMidPointOffset = GeometryEngine.Instance.GeodesicDistance(QAmidPoint, originalMidPoint);
                  if (datasetInGCS)
                    qaMidPointOffset = qaMidPointOffset/ 6378100.0 * 180.0 / Math.PI;

                  //report lines that have had a length change that exceeds normal expectations, or if the midpoint is offset from original.
                  //These are still written.
                  if (Math.Abs(newGeom.Length - originalPolyLineLength) > qaLengthTest ||
                    qaMidPointOffset > qaLengthTest)
                  {
                    sReportChangedLineList.Add("Line length was changed significantly, oid: " + oid.ToString());

                    if (!cogoLineLayer2ReportIds.ContainsKey(cogoLyr.Key))
                      cogoLineLayer2ReportIds.Add(cogoLyr.Key, lstReportOids);
                    lstReportOids.Add(oid);
                  }
                  else if (bOriginalIsSingleSegment & !bReversedGeometry)
                  {
                    geometryUnchangedCount++;
                    continue; //skip edits that are not needed                  
                  }

                  editOper.Modify(cogoLyr.Key, oid, newGeom);
                  updatedLineGeometryCount++;

                  if (cps.Progressor.CancellationToken.IsCancellationRequested)
                    break;
                }
              }
            }
            if (lstReportOids.Count > 0)
              cogoLineLayer2ReportIds[cogoLyr.Key] = lstReportOids;

            if (cps.Progressor.CancellationToken.IsCancellationRequested)
              break;
            cps.Progressor.Status = "Updating " + updatedLineGeometryCount + " lines ..." + Environment.NewLine +
              "Lines not changed: " + geometryUnchangedCount.ToString();
          }
          if (!editOper.IsEmpty)
            editOper.Execute();

          //if (ignoreCount > 0)
          //  return ignoreCount.ToString();
          
          return "";
        }
        catch (Exception ex)
        {
          if (ex.Message.Trim() == string.Empty)
            return "Unspecified error encountered.";
          else
            return ex.Message;
        }
      }, cps.Progressor);

      if (cps.Progressor.CancellationToken.IsCancellationRequested)
        return;

      int cnt = 0;
      foreach (string sReportLine in sReportChangedLineList)
      {
        cnt++;
        if (cnt <= maxReportedLines)
          sReportResult += sReportLine + Environment.NewLine;
        else
          break;
      }

      foreach (string sReportLine in sReportDisjointPointList)
      {
        cnt++;
        if (cnt <= maxReportedLines)
          sReportResult += sReportLine;
        else
          break;
      }

      cnt = sReportChangedLineList.Count + sReportDisjointPointList.Count; //re-use cnt variable

      if (!string.IsNullOrEmpty(errorMessage))
        MessageBox.Show(errorMessage, "Simplify By COGO Attributes");
      else if (!string.IsNullOrEmpty(sReportResult))
      {
        if (cnt >= maxReportedLines)
          sReportResult += Environment.NewLine + "Maximium report string reached."+ Environment.NewLine + 
            "Full list includes " + cnt.ToString() + " features." + Environment.NewLine;

        sReportResult += Environment.NewLine + "Do you want to select these line features?";
        if (MessageBox.Show(sReportResult, "Simplify By COGO Attributes", System.Windows.MessageBoxButton.YesNo) == System.Windows.MessageBoxResult.Yes)
        { //Select the reported line features
          await QueuedTask.Run(() =>
          {
            var map = MapView.Active?.Map;
            if (map == null)
              return;
            map.ClearSelection();
            var queryFilter = new QueryFilter();
            foreach (var reportFeatInfo in cogoLineLayer2ReportIds)
            {
              queryFilter.ObjectIDs = reportFeatInfo.Value;
              reportFeatInfo.Key.Select(queryFilter,SelectionCombinationMethod.Add);
            }
          });
        }
      }
    }
  }
}
