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
using ArcGIS.Desktop.Core.UnitFormats;
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

namespace ParcelsAddin
{
  internal class ParcelReportTool : MapTool
  {
    public ParcelReportTool()
    {
      IsSketchTool = true;
      SketchType = SketchGeometryType.Point;
      SketchOutputMode = SketchOutputMode.Map;
    }

    private DisplayUnitFormat _dialogDirectionUnit = null;
    private string _datasetUnitName = "meter";
    private double _datasetMetersPerUnit = 1.0;
    private bool _isPCS = true;

    private ParcelLayer _parcelFabricLayer;
    private List<FeatureLayer> _featureLayer;
    private readonly Dictionary<FeatureLayer, List<long>> dictLyr2IdsList = new ();

    private readonly ParcelReportViewModel _VM = new ();
    private readonly ConfigureParcelReportViewModel _configVM = new();
    private  CIMPointSymbol _pointSymbol;

    protected override Task OnToolActivateAsync(bool active)
    {
      //bool result = true;
      QueuedTask.Run(() =>
      {
        _parcelFabricLayer =
          MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
        if (_parcelFabricLayer == null)
        {
          //No parcel fabric found in the map.
          return Task.FromResult(true);
        }

        _pointSymbol = ParcelUtils.CreatePointSymbol();

        _featureLayer = new List<FeatureLayer>();
        var parcelTypes = _parcelFabricLayer.GetParcelTypeNamesAsync().Result;

        foreach (var parcelType in parcelTypes)
        {
          var fLyr = _parcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(parcelType).Result;
          if (fLyr != null)
            _featureLayer.AddRange(fLyr);
        }

        FeatureClassDefinition fcDefinition =
          _featureLayer[0].GetFeatureClass().GetDefinition();
        if (fcDefinition == null)
          return Task.FromResult(true);

        if (fcDefinition.GetSpatialReference()?.IsProjected ?? false)
        {
          _datasetUnitName = fcDefinition.GetSpatialReference()?.Unit.Name.ToLower();
          _datasetMetersPerUnit = fcDefinition.GetSpatialReference().Unit.ConversionFactor;
          _isPCS = true;
        }
        else
        {
          _datasetUnitName = "meter";
          _isPCS = false;
        }
        return Task.FromResult(true);
      });
      return Task.FromResult(true);
    }

    protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
    {
      double _distanceMetersPerLinearUnit = 1.0; //default to meters
      double _sqMetersPerAreaUnit = 1.0; //default to metric

      string _distanceUnitName = "meter";
      string areaUnitName = "sq.meter";

      ArcGIS.Core.SystemCore.DirectionType _directionType = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;

      bool useRadialDirection = false;
      bool useTangentDirection = false;
      bool useChordDirection = true;

      bool useProjectDirectionType = true;
      bool useProjectDistanceUnit = false;
      bool useDatasetDistanceUnit = true;

      bool useDMSSymbol = true;
      bool useDashes = false;
      bool useSpaces = false;

      bool useCommas = true;
      bool useColumns = false;

      int iDistPrec = 2;
      int iAreaPrec = 0;
      //draw 
      
      List<MapPoint> hintPoints = new();
      Task<IDisposable> _mapV = null;
      try
      {
        string sParamString = ConfigurationsLastUsed.Default["ConfigureParcelReportLastUsedParams"] as string;
        string[] sParams = sParamString.Split('|');
        //"<Project Units>|directiontypecode|<Dataset Units>|Chord|Radius And Arclength|Symbol [dd°mm'ss"]|Comma-separated"
        //"Symbols [dd°mm'ss\"]" , "Dashes [dd-mm-ss]", "Spaces [dd mm ss]"
        _ = long.TryParse(sParams[1], out long directionTypeCode);
        _distanceUnitName = sParams[2];
        _configVM.ConfigureParcelReportModel.DistanceUnitName = _distanceUnitName;
        _distanceMetersPerLinearUnit = _configVM.ConfigureParcelReportModel.MetersPerLinearUnit;
        iDistPrec = _configVM.ConfigureParcelReportModel.DistanceUnitPrecision;

        useProjectDirectionType = sParams[0].ToLower()== "<project units>";
        useProjectDistanceUnit = sParams[2].ToLower() == "<project units>";
        useDatasetDistanceUnit = sParams[2].ToLower() == "<dataset units>";

        useRadialDirection = sParams[3].ToLower() == "radial";
        useTangentDirection = sParams[3].ToLower() == "tangent";
        useChordDirection = sParams[3].ToLower() == "chord";

        useSpaces = sParams[5].ToLower() == "spaces [dd mm ss]";
        useDashes = sParams[5].ToLower() == "dashes [dd-mm-ss]";
        useDMSSymbol = sParams[5].ToLower() == "symbols [dd°mm'ss\"]";

        useColumns = sParams[6].ToLower() == "columns";
        useCommas = sParams[6].ToLower() == "comma-separated";

        if (!useProjectDirectionType)
          _directionType = (ArcGIS.Core.SystemCore.DirectionType)directionTypeCode;
      }
      catch
      {; }

      dictLyr2IdsList.Clear();
      string sReportResult = "";

      var ParcelReportDlg = new ParcelReportDialog
      {
        Owner = FrameworkApplication.Current.MainWindow,
        DataContext = _VM
      };
      var insp = new ArcGIS.Desktop.Editing.Attributes.Inspector();
      _ = QueuedTask.Run(async () =>
      {
        //get the direction format and units from the backstage default settings
        _dialogDirectionUnit =
          DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction);

        string sProjectDistanceUnitName = "";
        if (useProjectDistanceUnit)
        {
          _distanceMetersPerLinearUnit = COGOUtils.GetMetersPerUnitFromProject();
          sProjectDistanceUnitName = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance).MeasurementUnit.Name;
          var distUnitFormat = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance).UnitFormat as CIMNumericFormat;
          iDistPrec = distUnitFormat.RoundingValue;

          //for areas use the project area unit when the distance units are in project units
          _sqMetersPerAreaUnit = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Area).MeasurementUnit.ConversionFactor;
          areaUnitName = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Area).MeasurementUnit.Name;
          var areaUnitFormat = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Area).UnitFormat as CIMNumericFormat;
          iAreaPrec = areaUnitFormat.RoundingValue;

        }

        string sDistPrecision = "F" + iDistPrec.ToString();
        // define the spatial query filter
        var spatialQuery = new SpatialQueryFilter()
        {
          FilterGeometry = geometry,
          SpatialRelationship = SpatialRelationship.Intersects
        };

        foreach (var lyr in _featureLayer)
        {
          if (!lyr.IsVisibleInView(MapView.Active))
            continue;
          var fc = lyr.GetFeatureClass();
          List<long> lstOids = new();
          spatialQuery.WhereClause = lyr.DefinitionQuery;
          using (RowCursor rowCursor = fc.Search(spatialQuery))
          {
            while (rowCursor.MoveNext())
            {
              using (Row rowFeat = rowCursor.Current)
              {
                if (!dictLyr2IdsList.ContainsKey(lyr))
                  dictLyr2IdsList.Add(lyr, lstOids);
                lstOids.Add(rowFeat.GetObjectID());
              }
            }
          }
          if (lstOids.Count > 0)
            dictLyr2IdsList[lyr] = lstOids;
        }

        foreach (var featlyr in dictLyr2IdsList)
        {
          foreach (var oid in featlyr.Value)
          {
            insp.Load(featlyr.Key, oid);
            var sName = insp["Name"] as string;
            var polyGeom = insp["Shape"] as Geometry;

            ParcelEdgeCollection parcelEdgeCollection = null;
            try
            {
              var tol = 0.03 / _datasetMetersPerUnit;
              if (!_isPCS)
                tol = Math.Atan(tol/(6378100.0/ _datasetMetersPerUnit));

              var projectedGeom = GeometryEngine.Instance.Project(geometry,
                featlyr.Key.GetSpatialReference());
              var projectedPolygon = GeometryEngine.Instance.Project(polyGeom, featlyr.Key.GetSpatialReference());

              List<Segment> lst = new();
              ParcelUtils.SimplifyPolygonByLastAndFirstSegmentTangency(ref projectedPolygon, ref lst);

              var x = ParcelUtils.GetBendPointsFromGeometry(projectedPolygon);
              MapPoint hintPoint = ParcelUtils.FindNearestMapPointTo(x, projectedGeom as MapPoint, out double distance);
              hintPoints.Add(hintPoint);

              parcelEdgeCollection = 
                await _parcelFabricLayer.GetSequencedParcelEdgeInfoAsync(
                   featlyr.Key, oid, hintPoint, tol,
                   ParcelLineToEdgeRelationship.BothVerticesMatchAnEdgeEnd |
                   ParcelLineToEdgeRelationship.StartVertexMatchesAnEdgeEnd |
                   ParcelLineToEdgeRelationship.EndVertexMatchesAnEdgeEnd |
                   ParcelLineToEdgeRelationship.StartVertexOnAnEdge |
                   ParcelLineToEdgeRelationship.EndVertexOnAnEdge
                   ); //ParcelLineToEdgeRelationship.All);
            }
            catch(Exception ex)
            {
              sReportResult += "------------------------------------" + Environment.NewLine;
              sReportResult += "Layer: " + featlyr.Key.Name + Environment.NewLine;
              if (sName== String.Empty || sName == null)
                sReportResult += "Name: -- " + "(oid: " + oid.ToString() + ")" + Environment.NewLine;
              else
                sReportResult += "Name: " + sName + Environment.NewLine;
              sReportResult += "No lines found for parcel polygon." + Environment.NewLine;
              if (ex.Message != String.Empty && ex.Message!= "Value cannot be null. (Parameter 'source')")
                sReportResult += ex.Message + Environment.NewLine;
              continue;
            }
            if (parcelEdgeCollection == null)
              continue;

            if (!ParcelUtils.ParcelEdgeAnalysis(parcelEdgeCollection, out bool isClosedloop, out bool allLinesHaveCogo,
              out object[] parcelTraverseInfo))
              sReportResult += "No traverse information available.";

            var radiusList = new List<double>();
            foreach (var radiusObj in parcelTraverseInfo[3] as List<object>)
            {
              double radius = 0.0;
              if (radiusObj != null)
                radius = (double)radiusObj;
              radiusList.Add(radius);
            }

            var arcLengthList = new List<double>();
            foreach (var arcLengthObj in parcelTraverseInfo[4] as List<object>)
            {
              double arcLength = 0.0;
              if (arcLengthObj != null)
              {
                arcLength = (double)arcLengthObj;
              }
              arcLengthList.Add(arcLength);
            }

            var isMajorList = parcelTraverseInfo[5] as List<bool>;

            string sParcelName = 
            sReportResult += "------------------------------------" + Environment.NewLine;
            sReportResult += "Layer: " + featlyr.Key.Name + Environment.NewLine;
            if (sName == String.Empty || sName == null)
              sReportResult += "Name: -- " + "(oid: " + oid.ToString() + ")" + Environment.NewLine;
            else
              sReportResult += "Name: " + sName + Environment.NewLine;
            if (isClosedloop && allLinesHaveCogo)
            {
              #region line info strings for traverse
              var startPoint = parcelEdgeCollection.Edges[0].EdgeGeometry.Points[0].Coordinate2D;
              var traverseCourses = new List<Coordinate3D>();
              foreach (Coordinate3D vec in parcelTraverseInfo[0] as List<object>)
              {
                if (vec.Magnitude != 0.0)
                  traverseCourses.Add(vec);
              }

              var result = COGOUtils.CompassRuleAdjust(traverseCourses, startPoint, startPoint, radiusList, arcLengthList, isMajorList,
                out Coordinate2D miscloseVector, out double dRatio, out double cogoArea);

              bool bResult = COGOUtils.ComputeCircularArcParameters(traverseCourses, radiusList, arcLengthList,
                out List<double> radialDirectionList, out List<double> tangentDirectionList,
                out List<double> chordDistanceList, out List<double> centralAngleList);

              #region Misclose Direction and Distance
              var miscCloseDistance = miscloseVector.Magnitude;
              string miscloseDirectionString = "";
              var fromPoint = MapPointBuilderEx.CreateMapPoint(0.0, 0.0);
              var miscloseDirection = COGOUtils.InverseDirectionAsNorthAzimuth(fromPoint.Coordinate2D, 
                miscloseVector.ToMapPoint().Coordinate2D, true); //is reversed == true, consistent with ArcMap direction closure
              miscloseDirectionString = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(miscloseDirection, _dialogDirectionUnit,
                  useDMSSymbol); //default chord direction for circular arcs

              if (!useProjectDirectionType)
                miscloseDirectionString = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDirectionType(miscloseDirection, _directionType,
                   _dialogDirectionUnit, useDMSSymbol);

              if (useSpaces)
                miscloseDirectionString = miscloseDirectionString.Replace("-", " ").Replace("°", " ").Replace("'", " ").Replace("\"", "");

              var miscCloseDistanceInMeters = miscCloseDistance * _datasetMetersPerUnit;
              var cogoAreaInSqM = cogoArea * _datasetMetersPerUnit * _datasetMetersPerUnit;
              if (!useDatasetDistanceUnit)
              {
                miscCloseDistance = miscCloseDistanceInMeters / _distanceMetersPerLinearUnit;
                cogoArea = cogoAreaInSqM / (_distanceMetersPerLinearUnit * _distanceMetersPerLinearUnit);
              }
              #endregion

              if (useProjectDistanceUnit)
              //for areas use the project area unit when the distance units are in project units
                cogoArea /= _sqMetersPerAreaUnit;
              
              sReportResult += "COGO Area: " + cogoArea.ToString("F"+ iAreaPrec.ToString()) + Environment.NewLine;
              sReportResult += "Misclose ratio: 1:" + dRatio.ToString("F0") + Environment.NewLine;
              sReportResult += "Misclose: " + miscloseDirectionString + ", " + miscCloseDistance.ToString(sDistPrecision) + Environment.NewLine;

              sReportResult += "Clockwise lines:" + Environment.NewLine;
              var idx = 0;

              foreach (var vec in traverseCourses)
              {
                if (vec.Magnitude == 0.0)
                  continue; //ignore zero length vectors in report
                double directionParameter = vec.Azimuth * 180.0 / Math.PI;
                bool isCircularArc = radiusList[idx] != 0.0 && arcLengthList[idx] != 0.0;
                if (useTangentDirection && isCircularArc)
                  directionParameter = tangentDirectionList[idx];
                else if(useRadialDirection && isCircularArc)
                  directionParameter = radialDirectionList[idx];

                var direction = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(directionParameter, _dialogDirectionUnit,
                    useDMSSymbol); //default chord direction for circular arcs

                 if (!useProjectDirectionType)
                    direction = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDirectionType(directionParameter, _directionType,
                       _dialogDirectionUnit, useDMSSymbol);

                if (useSpaces)
                  direction = direction.Replace("-", " ").Replace("°", " ").Replace("'", " ").Replace("\"", "");

                var distance = vec.Magnitude;
                var distanceInMeters = distance * _datasetMetersPerUnit;
                if (!useDatasetDistanceUnit)
                  distance = distanceInMeters / _distanceMetersPerLinearUnit;


                if (radiusList[idx] == 0.0)
                {
                  if (useCommas)
                    sReportResult += "  " + direction + ", " + distance.ToString(sDistPrecision) + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{direction,15}\t{distance.ToString(sDistPrecision),10}" + Environment.NewLine;
                }
                else
                {
                  var radius = radiusList[idx];
                  var arclength = arcLengthList[idx];
                  var radiusInMeters = radius * _datasetMetersPerUnit;
                  var arclengthInMeters = arclength * _datasetMetersPerUnit;
                  if (!useDatasetDistanceUnit)
                  {
                    radius = radiusInMeters / _distanceMetersPerLinearUnit;
                    arclength=arclengthInMeters / _distanceMetersPerLinearUnit;
                  }

                  if (useCommas)
                    sReportResult += "  " + direction + ", " + distance.ToString(sDistPrecision) +
                      ", Radius: " + radius.ToString(sDistPrecision) + ", Arclength: " +
                      arclength.ToString(sDistPrecision) + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{direction,15}\t{distance.ToString(sDistPrecision),10}" +
                    $"{" ",-1}{radius.ToString(sDistPrecision),10} (r)" +
                    $"{" ",-1}{arclength.ToString(sDistPrecision),10} (al)" + Environment.NewLine;
                }
                idx++;
              }
              #endregion
            }
            else
            {
              if (isClosedloop && !allLinesHaveCogo)
                sReportResult += "Lines form a closed loop, but there is not enough COGO information to calculate misclose." + Environment.NewLine;
              else if (!isClosedloop && allLinesHaveCogo)
                sReportResult += "All lines found have COGO information, but they do not form a closed loop." + Environment.NewLine;
              else if (!isClosedloop && !allLinesHaveCogo)
                sReportResult += "Lines do not form a closed loop, and one or more lines are missing COGO information." + Environment.NewLine;

              #region line info strings for non-traverse
              int idx = 0;
              int iLeng = (parcelTraverseInfo[1] as List<object>).Count();
              string[] directionStr = new string[iLeng];
              string[] distanceStr = new string[iLeng];
              string[] radiusStr = new string[iLeng];
              string[] arcLengthStr = new string[iLeng];

              foreach (var dir in parcelTraverseInfo[1] as List<object>)
              {
                if (dir != null)
                {
                  var direction = (double)dir;
                  var sVal = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(direction, _dialogDirectionUnit, useDMSSymbol);
                  if (!useProjectDirectionType)
                    sVal = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDirectionType(direction, _directionType,
                       _dialogDirectionUnit, useDMSSymbol);

                  if (useSpaces)
                    sVal = sVal.Replace("-", " ").Replace("°", " ").Replace("'", " ").Replace("\"", "");

                  directionStr[idx++] = sVal;
                }
                else
                  directionStr[idx++] = "--";
              }
              idx = 0;
              foreach (var dist in parcelTraverseInfo[2] as List<object>)
              {
                if (dist != null)
                {
                  var distance = (double)dist;
                  var distanceInMeters = distance * _datasetMetersPerUnit;
                  if (!useDatasetDistanceUnit)
                    distance = distanceInMeters / _distanceMetersPerLinearUnit;
                  distanceStr[idx++] = distance.ToString(sDistPrecision);
                }
                else
                  distanceStr[idx++] = "--";
              }
              idx = 0;
              foreach (var rad in parcelTraverseInfo[3] as List<object>)
              {
                if (rad != null)
                {
                  var radius = (double)rad;
                  var radiusInMeters = radius * _datasetMetersPerUnit;
                  if (!useDatasetDistanceUnit)
                    radius = radiusInMeters / _distanceMetersPerLinearUnit;
                  radiusStr[idx++] = radius.ToString(sDistPrecision);
                }
                else
                  radiusStr[idx++] = "--";
              }
              idx = 0;
              foreach (var arc in parcelTraverseInfo[4] as List<object>)
              {
                if (arc != null)
                {
                  var arclength = (double)arc;
                  var arclengthInMeters = arclength * _datasetMetersPerUnit;
                  if (!useDatasetDistanceUnit)
                    arclength = arclengthInMeters / _distanceMetersPerLinearUnit;
                  arcLengthStr[idx++] = arclength.ToString(sDistPrecision);
                }
                else
                  arcLengthStr[idx++] = "--";
              }

              sReportResult += "Lines: " + Environment.NewLine;
              idx = 0;
              foreach (string dir in directionStr)
              {
                if (radiusStr[idx] == "--" && arcLengthStr[idx] == "--")
                {
                  if(useCommas)
                    sReportResult += "  " + dir + ", " + distanceStr[idx] + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{dir,15}\t{distanceStr[idx],10}" + Environment.NewLine;
                }
                else if (radiusStr[idx] != "--" && arcLengthStr[idx] == "--")
                {
                  if (useCommas)
                    sReportResult += "  " + dir + ", Radius: " + radiusStr[idx] +
                        ", " + arcLengthStr[idx] + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{dir,15}\t{"--",10}" +
                    $"{" ",-1}{radiusStr[idx],10} (r)" +
                    $"{" ",-1}{arcLengthStr[idx],10} (al)" + Environment.NewLine;
                }
                else if (radiusStr[idx] == "--" && arcLengthStr[idx] != "--")
                {
                  if (useCommas)
                    sReportResult += "  " + dir + ", " + radiusStr[idx] +
                        ", ArcLength: " + arcLengthStr[idx] + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{dir,15}\t{"--",10}" +
                    $"{" ",-1}{radiusStr[idx],10} (r)" +
                    $"{" ",-1}{arcLengthStr[idx],10} (al)" + Environment.NewLine;
                }
                else if (radiusStr[idx] != "--" && arcLengthStr[idx] != "--")
                {
                  if (useCommas)
                    sReportResult += "  " + dir + ", Radius: " + radiusStr[idx] +
                        ", ArcLength: " + arcLengthStr[idx] + Environment.NewLine;
                  else
                    sReportResult += $"{" ",-1}{dir,15}\t{"--",10}" +
                    $"{" ",-1}{radiusStr[idx],10} (r)" +
                    $"{" ",-1}{arcLengthStr[idx],10} (al)" + Environment.NewLine;
                }
                idx++;
              }
              #endregion
            }
          }
        }
        
        string sReportUnits = "Units: " + _datasetUnitName + ", sq." + _datasetUnitName + Environment.NewLine;
        if(!useProjectDistanceUnit && !useDatasetDistanceUnit)
          sReportUnits = "Units: " + _distanceUnitName.ToLower() + ", sq." + _distanceUnitName.ToLower() + Environment.NewLine;
        
        if(useProjectDistanceUnit)
          sReportUnits = "Units: " + sProjectDistanceUnitName.ToLower() + ", " + areaUnitName.ToLower() + Environment.NewLine;

        if (sReportResult.Trim().Length == 0)
          _VM.ParcelReport.ParcelReportText = "No parcels found. Please click on visible parcel polygons.";
        else
          _VM.ParcelReport.ParcelReportText = sReportUnits + sReportResult;
        
        //////draw : TODO
        ////var geom = MultipointBuilderEx.CreateMultipoint(hintPoints);
        ////_mapV = (Task<IDisposable>)MapView.Active.AddOverlay(geom, _pointSymbol.MakeSymbolReference());

      });

      ParcelReportDlg.ShowDialog();

      ////if(_mapV !=null)
      ////  _mapV.Dispose();//clear graphic
      
      return Task.FromResult(true);
    }

    //this code below is used for tool enablement, based on parcel selection
    //but is commented out for performance related reason. Fix TBD.

    //protected override void OnUpdate()
    //{
    //  QueuedTask.Run(() =>
    //  {
    //    //confirm we have a license...
    //    if (!ParcelUtils.HasValidLicenseForParcelLayer())
    //    {
    //      this.Enabled = false;
    //      this.DisabledTooltip = "Insufficient license level.";
    //      return;
    //    }
    //  });
    //}

  }
}