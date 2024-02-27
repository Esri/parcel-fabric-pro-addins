﻿using ArcGIS.Core.CIM;
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
  internal class SelectEllipticalArcs : Button
  {
    protected override async void OnClick()
    {
      CancelableProgressorSource cps = new("Select elliptical arcs", "Canceled");
      string sReportResult = "";
      string errorMessage = await QueuedTask.Run(() =>
      {
        var map = MapView.Active.Map;
        var mapSR = map.SpatialReference;

        Module1.GetVisibleFeatureLayers(MapView.Active, out List<FeatureLayer> visFeatLayers, false);
        try
        {
          foreach (FeatureLayer featLyr in visFeatLayers)
          {
            List<long> oIDs = new();
            cps.Progressor.Message = "Selecting " + featLyr.Name;
            //search for all features on the map in the selected feature layer
            var pSpatQuFilt = new SpatialQueryFilter();
            pSpatQuFilt.FilterGeometry = MapView.Active.Extent;
            pSpatQuFilt.SpatialRelationship = SpatialRelationship.Intersects;
            featLyr.Search(pSpatQuFilt);
            ICollection<Segment> segments = new List<Segment>();
            using (RowCursor rowCursor = featLyr.Search(pSpatQuFilt))
            {
              while (rowCursor.MoveNext())
              {
                using (Row row = rowCursor.Current)
                {
                  Polyline thePolyline = (row as Feature).GetShape() as Polyline;
                  Polygon thePolygon = (row as Feature).GetShape() as Polygon;

                  if (thePolyline == null && thePolygon == null)
                    continue;

                  if (thePolygon == null)
                    thePolyline.GetAllSegments(ref segments);
                  else if (thePolyline == null)
                    thePolygon.GetAllSegments(ref segments);

                  foreach (var seg in segments)
                  {
                    if (seg.SegmentType == SegmentType.EllipticArc) //collect all the elliptical arcs
                    {
                      if ((seg as EllipticArcSegment).MinorMajorRatio != 1.0)
                      {
                        oIDs.Add(row.GetObjectID());
                        cps.Progressor.Value += 1;
                        break; //in case there are multiple segments that are elliptical arcs on the same feature, we only need the first.
                      }
                      else 
                      {//check for the special case of circular arc imitation
                        //if CreatePolyline fails select the feature
                        try
                        {
                          Polyline seg2Polyline = PolylineBuilderEx.CreatePolyline(seg, mapSR);
                        }
                        catch
                        {
                          oIDs.Add(row.GetObjectID());
                          cps.Progressor.Value += 1;
                          break; //in case there are multiple segments that are elliptical arcs on the same feature, we only need the first.
                        }
                      }
                    }
                    if (cps.Progressor.CancellationToken.IsCancellationRequested)
                      break;
                  }
                  if (cps.Progressor.CancellationToken.IsCancellationRequested)
                    break;
                }
                cps.Progressor.Status = "Features with elliptical arcs: " + cps.Progressor.Value;
              }
            }
            QueryFilter pQuFilter = new();
            pQuFilter.ObjectIDs = oIDs;
            if (oIDs.Count > 0)
              featLyr.Select(pQuFilter, SelectionEnvironment.CombinationMethod);
          }
        }
        catch (Exception ex)
        {
          return ex.Message;
        }
        return "";
      }, cps.Progressor);

      if (!string.IsNullOrEmpty(errorMessage))
        MessageBox.Show(errorMessage, "Select Elliptical Arc Features");
      else if (!string.IsNullOrEmpty(sReportResult))
        MessageBox.Show(sReportResult, "Select Elliptical Arc Features");
    }
  }
}
