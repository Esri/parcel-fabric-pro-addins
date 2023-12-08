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
      dictLyr2IdsList.Clear();
      string sReportResult = "";
      var ParcelReportDlg = new ParcelReportDialog
      {
        Owner = FrameworkApplication.Current.MainWindow,
        DataContext = _VM
      };
      var insp = new ArcGIS.Desktop.Editing.Attributes.Inspector();
      QueuedTask.Run(async () =>
      {
        //get the direction format and units from the backstage default settings
        _dialogDirectionUnit =
          DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction);

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
            ParcelEdgeCollection parcelEdgeCollection = null;
            try
            {
              var tol = 0.03 / _datasetMetersPerUnit; //3 cms
              if (!_isPCS)
                tol = Math.Atan(tol/(6378100.0/ _datasetMetersPerUnit));

              var projectedGeom = GeometryEngine.Instance.Project(geometry,
                featlyr.Key.GetSpatialReference());

              parcelEdgeCollection = 
                await _parcelFabricLayer.GetSequencedParcelEdgeInfoAsync(
                   featlyr.Key, oid, projectedGeom as MapPoint, tol,
                   ParcelLineToEdgeRelationship.BothVerticesMatchAnEdgeEnd |
                   ParcelLineToEdgeRelationship.StartVertexMatchesAnEdgeEnd |
                   ParcelLineToEdgeRelationship.EndVertexMatchesAnEdgeEnd |
                   ParcelLineToEdgeRelationship.StartVertexOnAnEdge |
                   ParcelLineToEdgeRelationship.EndVertexOnAnEdge
                   ); //ParcelLineToEdgeRelationship.All);
            }
            catch(Exception ex)
            {
              sReportResult += "-----------------------" + Environment.NewLine;
              sReportResult += "Layer: " + featlyr.Key.Name + Environment.NewLine;
              if (sName== String.Empty || sName == null)
                sReportResult += "Name: -- " + "(oid: " + oid.ToString() + ")" + Environment.NewLine;
              else
                sReportResult += "Name: " + sName + Environment.NewLine;
              sReportResult += "No lines found for parcel polygon." + Environment.NewLine;
              if (ex.Message != String.Empty)
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
                arcLength = (double)arcLengthObj;
              arcLengthList.Add(arcLength);
            }

            var isMajorList = parcelTraverseInfo[5] as List<bool>;
            string sParcelName = 
            sReportResult += "-----------------------" + Environment.NewLine;
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
                traverseCourses.Add(vec);
              var result = COGOUtils.CompassRuleAdjust(traverseCourses, startPoint, startPoint, radiusList, arcLengthList, isMajorList,
                out Coordinate2D miscloseVector, out double dRatio, out double cogoArea);
              sReportResult += "Misclose ratio: 1:" + dRatio.ToString("F0") + Environment.NewLine;
              sReportResult += "COGO Area: " + cogoArea.ToString("F0") + Environment.NewLine;
              sReportResult += "Misclose distance: " + miscloseVector.Magnitude.ToString("F2") + Environment.NewLine;
              sReportResult += "Clockwise lines:" + Environment.NewLine;
              var idx = 0;
              foreach (var vec in traverseCourses)
              {
                var direction = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(vec.Azimuth * 180.0 / Math.PI, _dialogDirectionUnit);
                if (radiusList[idx] == 0.0)
                  sReportResult += "  " + direction + ", " + vec.Magnitude.ToString("F2") + Environment.NewLine;
                else
                {
                  sReportResult += "  " + direction + ", " + vec.Magnitude.ToString("F2") +
                    ", Radius: " + radiusList[idx].ToString("F2") + ", Arclength: " + arcLengthList[idx].ToString("F2") + Environment.NewLine;
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
                  var sVal = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(direction, _dialogDirectionUnit);
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
                  distanceStr[idx++] = distance.ToString("F2");
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
                  radiusStr[idx++] = radius.ToString("F2");
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
                  arcLengthStr[idx++] = arclength.ToString("F2");
                }
                else
                  arcLengthStr[idx++] = "--";
              }

              sReportResult += "Lines: " + Environment.NewLine;
              idx = 0;
              foreach (string dir in directionStr)
              {
                if (radiusStr[idx] == "--" && arcLengthStr[idx] == "--")
                  sReportResult += "  " + dir + ", " + distanceStr[idx] + Environment.NewLine;
                else if (radiusStr[idx] != "--" && arcLengthStr[idx] == "--")
                {
                  sReportResult += "  " + dir + ", Radius: " + radiusStr[idx] +
                        ", " + arcLengthStr[idx] + Environment.NewLine;
                }
                else if (radiusStr[idx] == "--" && arcLengthStr[idx] != "--")
                {
                  sReportResult += "  " + dir + ", " + radiusStr[idx] +
                        ", ArcLength: " + arcLengthStr[idx] + Environment.NewLine;
                }
                else if (radiusStr[idx] != "--" && arcLengthStr[idx] != "--")
                {
                  sReportResult += "  " + dir + ", Radius: " + radiusStr[idx] +
                        ", ArcLength: " + arcLengthStr[idx] + Environment.NewLine;
                }
                idx++;
              }
              #endregion
            }
          }
        }
        string sReportUnits = "Units: " + _datasetUnitName + ", sq." + _datasetUnitName + Environment.NewLine;
        if (sReportResult.Trim().Length == 0)
          _VM.ParcelReport.ParcelReportText = "No parcels found. Please click on visible parcel polygons.";
        else
          _VM.ParcelReport.ParcelReportText = sReportUnits + sReportResult;
      });
      ParcelReportDlg.ShowDialog();
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