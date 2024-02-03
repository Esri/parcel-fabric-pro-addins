/* Copyright 2023 Esri
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
using ArcGIS.Desktop.Core.UnitFormats;

namespace ParcelsAddin
{
  internal class UpdateCOGO : Button
  {
    //https://github.com/esri/arcgis-pro-sdk/wiki/ProConcepts-COGO#calculate-cogo-from-geometry
    protected async override void OnClick()
    {
      bool isControlledByFabric = false;
      CancelableProgressorSource cps = new("Update COGO Attributes", "Canceled");
      string reportMessage = await QueuedTask.Run(async () =>
      {
        try
        {
          var backstageDistanceUnit =
            DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance);
          double metersPerBackstageUnit = backstageDistanceUnit.MeasurementUnit.ConversionFactor;

          ConfigureUpdateCOGOViewModel updateCOGOVM = new(metersPerBackstageUnit);
          bool bUpdateDistances = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDistances;
          bool bDistancesUpdateAll = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDistancesOption[0];
          bool bDistancesUpdateOnlyNull = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDistancesOption[1];
          bool bDistancesUpdateByToleranceDifference = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDistancesOption[2];
          double dDistanceDifferenceToleranceInMeters = updateCOGOVM.ConfigureUpdateCOGOModel.DistanceDifferenceToleranceInMeters;

          if (dDistanceDifferenceToleranceInMeters < 0.001)
            dDistanceDifferenceToleranceInMeters = 0.001;

          bool bUpdateDirections = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDirections;
          bool bDirectionsUpdateAll = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[0];
          bool bDirectionsUpdateOnlyNull = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[1];
          bool bDirectionsUpdateByToleranceDifference = updateCOGOVM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[2];
          double dLateralOffsetToleranceInMeters = updateCOGOVM.ConfigureUpdateCOGOModel.LateralOffsetToleranceInMeters;
          if (dLateralOffsetToleranceInMeters < 0.001)
            dLateralOffsetToleranceInMeters = 0.001;
          double dDirectionDifferenceToleranceInDecDeg =
            updateCOGOVM.ConfigureUpdateCOGOModel.DifferenceDirectionToleranceDecimalDegrees;

          if (!COGOUtils.GetCOGOLineFeatureLayersSelection(MapView.Active,
              out Dictionary<FeatureLayer, List<long>> cogoLineLayerIds))
            return "Error getting COGO layer selections."; 

          //collect ground to grid correction values
          var mapView = MapView.Active;
          if (mapView?.Map == null)
            return "Could not find an active map.";
          var cimDefinition = mapView.Map?.GetDefinition();
          if (cimDefinition == null) return "Could not find an active map.";
          var cimG2G = cimDefinition.GroundToGridCorrection;

          var mapSpatRef = MapView.Active.Map.SpatialReference;

          double scaleFactor = cimG2G.GetConstantScaleFactor();
          double directionOffsetCorrection = cimG2G.GetDirectionOffset();

          var editOper = new EditOperation()
          {
            Name = "Update COGO Attributes",
            ShowModalMessageAfterFailure = true,
            ShowProgressor = true,
            SelectModifiedFeatures = false
          };
          Dictionary<string, object> COGOAttributes = new();

          foreach (var cogoLyr in cogoLineLayerIds)
          {
            var cogoLineFC = cogoLyr.Key.GetFeatureClass();
            var fcDefinition = cogoLineFC.GetDefinition();

            //check for field presence
            bool hasCOGOTypeFld = fcDefinition.FindField("cogotype") > -1;

            //check for field presence
            bool hasAzimuthTypeFld = fcDefinition.FindField("azimuthtype") > -1;
            GeodeticDirectionType azimuthType = GeodeticDirectionType.Grid; //default
            bool azimuthTypeIsNull = true;

            double datasetMetersPerUnit = 1.0;
            if (fcDefinition.GetSpatialReference().IsProjected)
              datasetMetersPerUnit = fcDefinition.GetSpatialReference().Unit.ConversionFactor;

            // Check if a layer has a parcel fabric source
            isControlledByFabric =
                 cogoLyr.Key.IsControlledByParcelFabricAsync(ParcelFabricType.ParcelFabric).Result;

            cps.Progressor.Message = "Updating " + cogoLyr.Key.ToString();
            foreach (var oid in cogoLyr.Value)
            {
              var insp = cogoLyr.Key.Inspect(oid);
              //check for valid feature
              var lineGeom = insp["shape"];
              if (lineGeom is not Polyline)
                continue;

              if (hasAzimuthTypeFld)
              {
                var currentAzimuthType = insp["azimuthtype"];
                if (currentAzimuthType != DBNull.Value)
                {
                  //int value = (int)Enum.Parse(typeof(TestAppAreana.MovieList.Movies), KeyVal);
                  azimuthType = (GeodeticDirectionType)(int)currentAzimuthType;
                  azimuthTypeIsNull = false;
                }
                else
                  azimuthType = GeodeticDirectionType.Grid;
              }

              var currentDirObj = insp["direction"];
              var currentDistObj = insp["distance"];
              var currentRadiusObj = insp["radius"];
              var currentArclengthObj = insp["arclength"];

              bool hasNullDistLengthAndRadius =
                currentDistObj == DBNull.Value && currentArclengthObj == DBNull.Value
                  && currentRadiusObj == DBNull.Value;

              bool isCircularArc = currentRadiusObj != DBNull.Value && currentArclengthObj != DBNull.Value;

              //Special cases: check for spiral, or flat circular arcs
              //In these cases only the direction is updated
              var r2 = insp["radius2"];

              bool specialCaseUpdateDirectionOnly = r2 != DBNull.Value;
              if (!specialCaseUpdateDirectionOnly && currentArclengthObj != DBNull.Value
                  && currentRadiusObj != DBNull.Value)
              {
                ICollection<Segment> LineSegments = new List<Segment>();
                (lineGeom as Polyline).GetAllSegments(ref LineSegments);
                int numSegments = LineSegments.Count;
                IList<Segment> iList = LineSegments as IList<Segment>;
                for (int i = 0; i < numSegments; i++)
                {
                  var pCircArc = iList[i] as EllipticArcSegment;
                  if (pCircArc == null)
                    break;

                  if ((double)currentRadiusObj == 0)
                  {
                    specialCaseUpdateDirectionOnly = true;
                    break;
                  }
                  specialCaseUpdateDirectionOnly = 
                    (180.0/Math.PI * Math.Abs((double)currentArclengthObj / (double)currentRadiusObj))<1.0;
                  //central angle based on COGO is less than 1° means flat circular arc
                }
              }

              if (!COGOUtils.GetCOGOFromGeometry((Polyline)lineGeom, mapSpatRef, scaleFactor, directionOffsetCorrection,
                       out object[] COGODirectionDistanceRadiusArcLength, azimuthType))
              {
                editOper.Abort();
                return "Edit operation failed.";
              }

              if (bUpdateDirections && (bDirectionsUpdateAll ||
                    (bDirectionsUpdateByToleranceDifference && currentDirObj == DBNull.Value)))
              {
                COGOAttributes.Add("direction", COGODirectionDistanceRadiusArcLength[0]);

                if (isControlledByFabric)
                {
                  if (hasCOGOTypeFld) COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                  COGOAttributes.Add("rotation", directionOffsetCorrection);
                }
              }
              else if (bUpdateDirections &&
                      (bDirectionsUpdateOnlyNull || bDirectionsUpdateByToleranceDifference))
              {
                if (currentDirObj == DBNull.Value && bDirectionsUpdateOnlyNull)
                {
                  COGOAttributes.Add("direction", COGODirectionDistanceRadiusArcLength[0]);
                  if (isControlledByFabric)
                  {
                    if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                      COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                    COGOAttributes.Add("rotation", directionOffsetCorrection);
                  }
                }
                else if (bDirectionsUpdateByToleranceDifference && currentDirObj != DBNull.Value)
                {
                  //test the difference tolerance
                  double currentDirection = (double)currentDirObj;
                  double incomingDirection = (double)COGODirectionDistanceRadiusArcLength[0];
                  double diff =
                    Math.Abs(COGOUtils.AngleDifferenceBetweenDirections(currentDirection, incomingDirection));
                  double diffRadians = diff * Math.PI / 180.0;
                  //compute the lateral offset for the angle difference
                  double geomDist = COGOUtils.StraightLineStartPointToEndPointDistance(lineGeom as Polyline);
                  //UNIT conversion to meters for lateral offset test
                  double lateralOffset = Math.Abs(geomDist * Math.Tan(diffRadians)) * datasetMetersPerUnit;

                  //if both diff and lateralOffset values are larger than user-entered, then update
                  if (diff > Math.Abs(dDirectionDifferenceToleranceInDecDeg) &&
                    lateralOffset > Math.Abs(dLateralOffsetToleranceInMeters))
                  {
                    COGOAttributes.Add("direction", COGODirectionDistanceRadiusArcLength[0]);
                    if (isControlledByFabric)
                    {
                      if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                        COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                      COGOAttributes.Add("rotation", directionOffsetCorrection);
                    }
                  }
                }
              }

              if (!specialCaseUpdateDirectionOnly)
              {
                if (bUpdateDistances && (bDistancesUpdateAll ||
                       (bDistancesUpdateByToleranceDifference && hasNullDistLengthAndRadius)))
                {
                  COGOAttributes.Add("distance", COGODirectionDistanceRadiusArcLength[1]);
                  COGOAttributes.Add("radius", COGODirectionDistanceRadiusArcLength[2]);
                  COGOAttributes.Add("arclength", COGODirectionDistanceRadiusArcLength[3]);
                  if (isControlledByFabric)
                  {
                    if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                      COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                    COGOAttributes.Add("scale", scaleFactor);
                    COGOAttributes.Add("iscogoground", 1);
                  }
                }
                else if (bUpdateDistances &&
                        (bDistancesUpdateOnlyNull || bDistancesUpdateByToleranceDifference))
                {
                  if (currentDistObj == DBNull.Value && bDistancesUpdateOnlyNull)
                  {
                    COGOAttributes.Add("distance", COGODirectionDistanceRadiusArcLength[1]);
                    COGOAttributes.Add("radius", COGODirectionDistanceRadiusArcLength[2]);
                    COGOAttributes.Add("arclength", COGODirectionDistanceRadiusArcLength[3]);
                    if (isControlledByFabric)
                    {
                      if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                        COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                      COGOAttributes.Add("scale", scaleFactor);
                      COGOAttributes.Add("iscogoground", 1);
                    }
                  }
                  else if (bDistancesUpdateByToleranceDifference && currentDistObj != DBNull.Value &&
                    COGODirectionDistanceRadiusArcLength[1] != DBNull.Value)
                  {
                    //test the difference tolerances
                    if (!isCircularArc)
                    {//Straight line. Values are in dataset units.
                      double currentDistance = (double)currentDistObj;
                      double incomingDistance = (double)COGODirectionDistanceRadiusArcLength[1];
                      //UNIT conversion to meters for difference tolerance check.
                      double diff = Math.Abs(currentDistance - incomingDistance) * datasetMetersPerUnit;
                      //if diff value is larger than user-entered, then update
                      if (diff > dDistanceDifferenceToleranceInMeters)
                      {
                        COGOAttributes.Add("distance", COGODirectionDistanceRadiusArcLength[1]);
                        if (isControlledByFabric)
                        {
                          if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                            COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                          COGOAttributes.Add("scale", scaleFactor);
                          COGOAttributes.Add("iscogoground", 1);
                        }
                      }
                    }
                    else if (isCircularArc)
                    {// Circular arc. Values are in dataset units.
                      double currentArclength = (double)currentArclengthObj;
                      double incomingArclength = (double)COGODirectionDistanceRadiusArcLength[3];
                      //UNIT conversion to meters for checking difference tolerance
                      double diff = Math.Abs(currentArclength - incomingArclength) * datasetMetersPerUnit;
                      //if diff value is larger than user-entered, then update
                      if (diff > dDistanceDifferenceToleranceInMeters)
                      {
                        COGOAttributes.Add("radius", COGODirectionDistanceRadiusArcLength[2]);
                        COGOAttributes.Add("arclength", COGODirectionDistanceRadiusArcLength[3]);
                        if (isControlledByFabric)
                        {
                          if (hasCOGOTypeFld && !COGOAttributes.ContainsKey("cogotype"))
                            COGOAttributes.Add("cogotype", 2); // always 'FromGeometry'
                          COGOAttributes.Add("scale", scaleFactor);
                          COGOAttributes.Add("iscogoground", 1);
                        }
                      }
                    }
                  }
                }
              }

              if (hasAzimuthTypeFld && azimuthTypeIsNull)
              {//only write this field if it starts out as null
                COGOAttributes.Add("azimuthtype", azimuthType);
              }

              cps.Progressor.Value += 1;
              editOper.Modify(cogoLyr.Key, oid, COGOAttributes);
              COGOAttributes.Clear();

              if (cps.Progressor.CancellationToken.IsCancellationRequested)
                break;
            }
            if (cps.Progressor.CancellationToken.IsCancellationRequested)
              break;
            cps.Progressor.Status = "Lines updated: " + cps.Progressor.Value;
          }
          if (!editOper.IsEmpty)
            editOper.Execute();
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
    }
  }

}
