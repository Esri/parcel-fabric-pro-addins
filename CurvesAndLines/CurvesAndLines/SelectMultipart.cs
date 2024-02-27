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
  internal class SelectMultipart : Button
  {
    protected override async void OnClick()
    {
      CancelableProgressorSource cps = new("Select features with multiparts", "Canceled");
      string sReportResult = "";
      string errorMessage = await QueuedTask.Run(() =>
      {
        Module1.GetVisibleFeatureLayers(MapView.Active, out List<FeatureLayer> visFeatLayers, false);
        try
        {
          foreach (FeatureLayer featLyr in visFeatLayers)
          {
            List<long> oIDs = new();
            cps.Progressor.Message = "Selecting " + featLyr.Name;

            //search for all visible features in the map extent
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

                  var partCount = 1;
                  if (thePolygon == null)
                    partCount = thePolyline.PartCount;
                  else if (thePolyline == null)
                    partCount = thePolygon.PartCount;

                  if (partCount > 1)
                  {
                    oIDs.Add(row.GetObjectID());
                    cps.Progressor.Value += 1;                  
                  }
                  if (cps.Progressor.CancellationToken.IsCancellationRequested)
                    break;
                }
                cps.Progressor.Status = "Features with multipart geometry: " + cps.Progressor.Value;
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
        MessageBox.Show(errorMessage, "Select Multipart Features");
      else if (!string.IsNullOrEmpty(sReportResult))
        MessageBox.Show(sReportResult, "Select Multipart Features");
    }
  }
}
