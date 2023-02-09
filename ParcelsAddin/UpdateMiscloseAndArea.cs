/*   Copyright 2023 Esri
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.
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

namespace ParcelsAddin
{
  internal class UpdateMiscloseAndArea : Button
  {
    private List<FeatureLayer> _featureLayer;
    private readonly ParcelUtils _parcelUtils = new();
    private double _minimumAcreStatedAreaTolerance = 0.25; //switch area unit to acres if greater than or equal to 1/4 acre

    protected override void OnUpdate()
    {
      QueuedTask.Run(() =>
      {
        var myParcelFabricLayer =
        MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();

        //if there is no fabric in the map then bail
        if (myParcelFabricLayer == null)
        {
          this.Enabled = false;
          this.DisabledTooltip = "There is no fabric in the map.";
          return;
        }
        if (_parcelUtils.HasParcelSelection(myParcelFabricLayer))
        {
          this.Enabled = true;  //tool is enabled  
                                //this.Tooltip = "";
        }
        else
        {
          this.Enabled = false;  //tool is disabled  
                                 //customize your disabledText here
          this.DisabledTooltip = "There is no parcel selection.";
        }
      });
    }

    protected override async void OnClick()
    {
      //first confirm we have a license...
      var lic = ArcGIS.Core.Licensing.LicenseInformation.Level;
      if (lic < ArcGIS.Core.Licensing.LicenseLevels.Standard)
      {
        MessageBox.Show("Insufficient License Level.");
        return;
      }

      double _largeParcelToleranceInSqMeters = 1011.715; //default to quarter acre in units of meters = 1011.715 sq.m
      long _largeParcelUnitCode = 109402; //acres as default
      double _sqMetersPerAreaUnit = 4046.86;//for acres as default
      
      try
      {
        string sParamString = ConfigureAreaUnitsDlg.Default["LastUsedParams"] as string;
        string[] sParams = sParamString.Split('|'); //"Acres|0.25|area unit code|1011.715"

        long.TryParse(sParams[2], out _largeParcelUnitCode);
        Double.TryParse(sParams[3], out _largeParcelToleranceInSqMeters);
        Double.TryParse(sParams[4], out _sqMetersPerAreaUnit);
      }
      catch
      {; }
      var cgUtils = new COGOUtils();

      Dictionary<FeatureLayer, List<long>> dictLyr2IdsList = new Dictionary<FeatureLayer, List<long>>();
      string errorMessage = await QueuedTask.Run(async () =>
      {
        var myParcelFabricLayer =
          MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
        //if there is no fabric in the map then bail
        if (myParcelFabricLayer == null)
          return "There is no fabric layer in the map.";

        var recordsLyr = myParcelFabricLayer.GetRecordsLayerAsync().Result.FirstOrDefault();
        if (recordsLyr == null)
          return "There is no records layer in the map.";
        //...confirm we're not editing default version geodatabase.
        if (_parcelUtils.IsDefaultVersionOnFeatureService(recordsLyr))
          return "Editing on the default version is not available.";

        double _metersPerUnit = 1;

        bool _isPCS = true;
        var pSR = myParcelFabricLayer.GetSpatialReference();
        if (pSR.IsProjected)
          _metersPerUnit = pSR.Unit.ConversionFactor; //meters per unit    
        else
          _isPCS = false;

        _featureLayer = new List<FeatureLayer>();
        var parcelTypes = myParcelFabricLayer.GetParcelTypeNamesAsync().Result;

        foreach (var parcelType in parcelTypes)
        {
          var fLyr = myParcelFabricLayer.GetParcelPolygonLayerByTypeNameAsync(parcelType).Result.FirstOrDefault();
          if (fLyr != null)
          {
            if (fLyr.IsVisible)
              _featureLayer.Add(fLyr);
          }
        }

        dictLyr2IdsList.Clear();
        foreach (var lyr in _featureLayer)
        {
          var fc = lyr.GetFeatureClass();
          List<long> lstOids = new();
          using (RowCursor rowCursor = lyr.GetSelection().Search())
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

        var editOper = new EditOperation()
        {
          Name = "Update Misclose and Area",
          ShowModalMessageAfterFailure = true,
          SelectModifiedFeatures = false
        };

        foreach (var featlyr in dictLyr2IdsList)
        {
          foreach (var oid in featlyr.Value)
          {
            ParcelEdgeCollection parcelEdgeCollection = null;
            var tol = 0.03 / _metersPerUnit; //3 cms
            if (!_isPCS)
              tol = Math.Atan(tol / (6378100 / _metersPerUnit));
            try
            {
              parcelEdgeCollection = await myParcelFabricLayer.GetSequencedParcelEdgeInfoAsync(featlyr.Key,
                  oid, null, tol,
                  ParcelLineToEdgeRelationship.BothVerticesMatchAnEdgeEnd |
                  ParcelLineToEdgeRelationship.StartVertexMatchesAnEdgeEnd |
                  ParcelLineToEdgeRelationship.EndVertexMatchesAnEdgeEnd |
                  ParcelLineToEdgeRelationship.StartVertexOnAnEdge |
                  ParcelLineToEdgeRelationship.EndVertexOnAnEdge
                  ); //ParcelLineToEdgeRelationship.All);
            }
            catch
            {
              continue;
            }

            if (parcelEdgeCollection == null)
              continue;

            var startPoint = parcelEdgeCollection.Edges[0].EdgeGeometry.Points[0].Coordinate2D;
            var radiusList = new List<double>();
            var arcLengthList = new List<double>();
            var isMajorList = new List<bool>();
            var traverseCourses = new List<Coordinate3D>();
            bool canTraverseCOGO = true; //optimistic outer
            foreach (var edge in parcelEdgeCollection.Edges)
            {
              bool edgeHasCOGOConnectivity = false; //pessimistic inner
              var highestPosition = 0.0;
              foreach (var myLineInfo in edge.Lines)
              {
                //test for COGO attributes and line type
                bool hasCOGODirection = myLineInfo.FeatureAttributes.TryGetValue("Direction", out object direction);
                bool hasCOGODistance = myLineInfo.FeatureAttributes.TryGetValue("Distance", out object distance);
                bool hasCOGORadius = myLineInfo.FeatureAttributes.TryGetValue("Radius", out object radius);
                bool hasCOGOArclength = myLineInfo.FeatureAttributes.TryGetValue("ArcLength", out object arclength);
                bool bIsCOGOLine = hasCOGODirection && hasCOGODistance;

                //logic to exclude unwanted lines on this edge
                if (!myLineInfo.HasNextLineConnectivity)
                  continue;
                if (myLineInfo.EndPositionOnParcelEdge > 1)
                  continue;
                if (myLineInfo.EndPositionOnParcelEdge < 0)
                  continue;
                if (myLineInfo.StartPositionOnParcelEdge > 1)
                  continue;
                if (myLineInfo.StartPositionOnParcelEdge < 0)
                  continue;
                //also exclude historic lines
                bool hasRetiredByGuid = myLineInfo.FeatureAttributes.TryGetValue("RetiredByRecord", out object guid);
                if (hasRetiredByGuid && guid != DBNull.Value)
                  continue;

                if (!bIsCOGOLine)
                {
                  if (hasCOGODirection && hasCOGORadius && hasCOGOArclength) //circular arc
                  {
                    edgeHasCOGOConnectivity = true;
                    var dRadius = (double)radius;
                    var dArclength = (double)arclength;
                    double dCentralAngle = dArclength / dRadius;
                    var chordDistance = 2.0 * dRadius * Math.Sin(dCentralAngle / 2.0);
                    var flip = myLineInfo.IsReversed ? Math.PI : 0.0;

                    var radiansDirection = ((double)direction * Math.PI / 180.0) + flip;
                    Coordinate3D vect = new ();
                    vect.SetPolarComponents(radiansDirection, 0.0, chordDistance);
                    if (_parcelUtils.ClockwiseDownStreamEdgePosition(myLineInfo) == highestPosition)
                    {//this line's start matches last line's end
                      traverseCourses.Add(vect);
                      arcLengthList.Add(dArclength);
                      if (Math.Abs(dArclength / dRadius) > Math.PI)
                        isMajorList.Add(true);
                      else
                        isMajorList.Add(false);
                      if (myLineInfo.IsReversed)
                        radiusList.Add(-dRadius); //this is for properly calcluating area sector
                      else
                        radiusList.Add(dRadius);
                    }
                  }
                  else //not a cogo circular arc, nor a cogo line
                    continue;
                }
                else //this is a straight cogo line
                {
                  var flip = myLineInfo.IsReversed ? Math.PI : 0.0;
                  edgeHasCOGOConnectivity = true;
                  var radiansDirection = ((double)direction * Math.PI / 180.0) + flip;
                  Coordinate3D vect = new();
                  vect.SetPolarComponents(radiansDirection, 0.0, (double)distance);
                  if (_parcelUtils.ClockwiseDownStreamEdgePosition(myLineInfo) == highestPosition)
                  {//this line's start matches last line's end
                    traverseCourses.Add(vect);
                    arcLengthList.Add(0.0);
                    radiusList.Add(0.0);
                    isMajorList.Add(false);
                  }
                }
                if (edgeHasCOGOConnectivity)
                {
                  var UpstreamPos = _parcelUtils.ClockwiseUpStreamEdgePosition(myLineInfo);
                  highestPosition =
                    highestPosition > UpstreamPos ? highestPosition : UpstreamPos;
                }
              }
              if (highestPosition != 1)
              //means we were not able to traverse all the way to the end of this edge without a loss of COGO connection
              {
                canTraverseCOGO = false;
                break;
              }
            }
            if (canTraverseCOGO)
            {
              var result = cgUtils.CompassRuleAdjust(traverseCourses, startPoint, startPoint, radiusList, arcLengthList, isMajorList,
                out Coordinate2D miscloseVector, out double dRatio, out double calcArea);
              Dictionary<string, object> ParcelAttributes = new Dictionary<string, object>();

              ParcelAttributes.Add("MiscloseDistance", miscloseVector.Magnitude);
              ParcelAttributes.Add("MiscloseRatio", dRatio);
              if (!Double.IsNaN(calcArea))
              {
                ParcelAttributes.Add("CalculatedArea", calcArea);
                var AreaSqMeters = calcArea * _metersPerUnit * _metersPerUnit;
                if (AreaSqMeters >= _largeParcelToleranceInSqMeters)
                {
                  var areaInLargeParcelUnits = AreaSqMeters / _sqMetersPerAreaUnit;// example: 1 acre = 4046.86 sq.meters
                  ParcelAttributes.Add("StatedArea", areaInLargeParcelUnits.ToString("0.00"));
                  ParcelAttributes.Add("StatedAreaUnit", _largeParcelUnitCode);
                }
                else
                {
                  ParcelAttributes.Add("StatedArea", calcArea.ToString("0"));
                  if (_metersPerUnit < 1)
                    ParcelAttributes.Add("StatedAreaUnit", 109405); //use square foot
                  else
                    ParcelAttributes.Add("StatedAreaUnit", 109404); //use square meter
                }
              }
              editOper.Modify(featlyr.Key, oid, ParcelAttributes);
              ParcelAttributes.Clear();
            }
          }
        }
        if (!editOper.IsEmpty)
          editOper.Execute();
        return "";
      });
    }
  }
}
