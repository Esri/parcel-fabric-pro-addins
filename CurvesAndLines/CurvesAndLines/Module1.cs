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

    internal static void GetVisibleFeatureLayers(MapView mapView, 
      out List<FeatureLayer> featureVisibleLayers, 
      bool IncludePointLayers = true, bool IncludeLineLayers = true, bool IncludePolygonLayers = true, 
      bool IgnoreTopologyLayers = true)
    {
      List<FeatureLayer> featureLayers = 
        mapView.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>().ToList();
      
      featureVisibleLayers = new();
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
        GeometryType geomType = fc.GetDefinition().GetShapeType();

        if (!featureVisibleLayers.Contains(lyr))
        {
          if (lyr.IsVisibleInView(mapView))
          {
            if(geomType == GeometryType.Point && IncludePointLayers)
              featureVisibleLayers.Add(lyr);
            if (geomType == GeometryType.Polyline && IncludeLineLayers)
              featureVisibleLayers.Add(lyr);
            if (geomType == GeometryType.Polygon && IncludePolygonLayers)
              featureVisibleLayers.Add(lyr);
          }
        }
      }
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
