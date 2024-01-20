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
using ArcGIS.Desktop.Editing.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GroundToGridFromActiveRecord
{
  internal class Module1 : Module
  {
    private static Module1 _this = null;

    internal Module1()
    {
      ActiveParcelRecordChangingEvent.Subscribe(ActiveRecordEventMethod);
    }

    ~Module1()
    {
      ActiveParcelRecordChangingEvent.Unsubscribe(ActiveRecordEventMethod);
    }
    private async void ActiveRecordEventMethod(ParcelRecordEventArgs e)
    {
      var theNewActiveRecord = e.IncomingActiveRecord;
      var newRecName = theNewActiveRecord?.Name;

      var thePreviousActiveRecord = e.OutgoingActiveRecord;
      var prevRecName = thePreviousActiveRecord?.Name;

      if (newRecName == null && prevRecName == null)
        return;
      //Get the active map view
      var mapView = MapView.Active;
      if (mapView?.Map == null)
        return;

      //Get the fabric
      var fabricLyr = mapView.Map.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
      if (fabricLyr == null)
        return;

      //get the Records layer
      var recordsLyr = fabricLyr.GetRecordsLayerAsync().Result.FirstOrDefault();
      if (recordsLyr == null)
        return;

      if (theNewActiveRecord == null)
        return;

      List<long> lst = new();
      lst.Add(theNewActiveRecord.ObjectID);
      var QueryFilter = new QueryFilter { ObjectIDs = lst.ToArray() };
      var cim_g2g = await mapView.Map.GetGroundToGridCorrection();
      if (cim_g2g == null)
        cim_g2g = new CIMGroundToGridCorrection(); // CIM for ground to grid is null for new maps, so initialize it for the first time here.

      object oScaleFactor = null;
      object oDirectionOffset = null;
      using RowCursor rowCursor = recordsLyr.Search(QueryFilter);
      while (rowCursor.MoveNext()) //should only be one record
      {
        //changing the G2G settings
        var iFldDistanceFactor = rowCursor.FindField("distancefactor");
        var iFldDirectionOffset = rowCursor.FindField("directionoffset");

        if (iFldDistanceFactor == -1 && iFldDirectionOffset == -1)
          return;

        using Row rowRec = rowCursor.Current;
        if (iFldDistanceFactor != -1)
        {
          oScaleFactor = rowRec[iFldDistanceFactor];
          if (oScaleFactor != null)
          {
            cim_g2g.ConstantScaleFactor = Convert.ToDouble(oScaleFactor);
            cim_g2g.Enabled = true;
            cim_g2g.UseScale = true;
            cim_g2g.ScaleType = GroundToGridScaleType.ConstantFactor;
          }
          else
            cim_g2g.UseScale = false;
        }
        if (iFldDirectionOffset != -1)
        {
          oDirectionOffset = rowRec[iFldDirectionOffset];
          if (oDirectionOffset != null)
          {
            cim_g2g.Direction = Convert.ToDouble(oDirectionOffset);
            // store and set this in decimal degrees, irrespective of project unit settings
            cim_g2g.Enabled = true;
            cim_g2g.UseDirection = true;
          }
          else
            cim_g2g.UseDirection = false;
        }
      }
      if (oScaleFactor == null && oDirectionOffset == null)
        return;//if both direction offset and scale are null, do nothing

      await mapView.Map.SetGroundToGridCorrection(cim_g2g);
    }


    /// <summary>
    /// Retrieve the singleton instance to this module here
    /// </summary>
    public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("GroundToGridFromActiveRecord_Module");

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

  }
}
