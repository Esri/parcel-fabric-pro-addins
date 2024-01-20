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

namespace GroundToGridFromActiveRecord
{
  internal class StoreGroundToGridOnActiveRecord : Button
  {
    private CIMGroundToGridCorrection _cimG2G;
    private ParcelLayer _parcelFabricLayer;
    private ParcelRecord _activeParcelRecord;

    internal StoreGroundToGridOnActiveRecord()
    {
      GetParcelLayer();
    }

    protected override async void OnClick()
    {
      var scale = _cimG2G.GetConstantScaleFactor();
      var rotation = _cimG2G.GetDirectionOffset();
      List<string> lstFlds = new();
      await QueuedTask.Run(() =>
      {
        //get the Records layer
        FeatureLayer recordsLyr = _parcelFabricLayer.GetRecordsLayerAsync().Result.FirstOrDefault();
        if (recordsLyr == null)
          return;
        recordsLyr.GetFieldDescriptions().ForEach(desc => lstFlds.Add(desc.Name.ToLower()));

        if (!lstFlds.Contains("distancefactor") && !lstFlds.Contains("directionoffset"))
        {
          MessageBox.Show("The required fields were not found." + Environment.NewLine +
            "This add-in requires two [double] fields on the Records feature class called " +
            "'DirectionOffset' and 'DistanceFactor'.", "Save Ground To Grid To Record");
          return;
        }
        //var insp = recordsLyr.Inspect(_activeParcelRecord.ObjectID);
        Dictionary<string, Object> recordAttributes = new();
        if (lstFlds.Contains("distancefactor"))
          recordAttributes.Add("distancefactor", scale);
        else
          recordAttributes.Add("distancefactor", DBNull.Value);

        if (lstFlds.Contains("directionoffset"))
          recordAttributes.Add("directionoffset", rotation);
        else
          recordAttributes.Add("directionoffset", DBNull.Value);

        var editOper = new EditOperation()
        {
          Name = "Save Ground To Grid To Record",
          ShowModalMessageAfterFailure = true
        };

        editOper.Modify(recordsLyr, _activeParcelRecord.ObjectID, recordAttributes);
        recordAttributes.Clear();
        if (!editOper.Execute())
          editOper.Abort();
      });
    }

    internal void GetParcelLayer()
    {
      _parcelFabricLayer =
        MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
    }

    protected override void OnUpdate()
    {
      this.Enabled = true; //start out true
      GetParcelLayer();
      //if there is no fabric in the map then bail
      if (_parcelFabricLayer == null)
      {
        this.Enabled = false;
        this.DisabledTooltip = "There is no fabric in the map.";
        return;
      }
      QueuedTask.Run(async () =>
      {
        var mapView = MapView.Active;
        _cimG2G = await mapView.Map.GetGroundToGridCorrection();
      });

      if (_cimG2G == null)
      {
        this.Enabled = false;
        this.DisabledTooltip = "Ground to grid is not set.";
        return;
      }
      if (!_cimG2G.Enabled)
      {
        this.Enabled = false;
        this.DisabledTooltip = "Ground to grid is not turned on.";
        return;
      }
      _activeParcelRecord = _parcelFabricLayer.GetActiveRecord();

      if (_activeParcelRecord == null)
      {
        this.Enabled = false;
        this.DisabledTooltip = "There is no active record set.";
        return;
      }
    }
  }
}
