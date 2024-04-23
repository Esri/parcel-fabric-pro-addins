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
using ArcGIS.Desktop.Mapping.Events;
using ArcGIS.Desktop.Editing.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Desktop.Framework.Events;
using ArcGIS.Desktop.Core.Events;

namespace GroundToGridFromActiveRecord
{
  internal class Module1 : Module
  {
    private static Module1 _this = null;
    private static short _mapOpenExpectedEventCount = 1;
    private static short _activeRecordEventCount = 0;

    private async void ActiveRecordEventMethod(ParcelRecordEventArgs e)
    {
      try
      {
        //avoid event firing on changing active maps (see MapInitializeEventMethod)
        _activeRecordEventCount++;
        if (_activeRecordEventCount <= _mapOpenExpectedEventCount)
          return;
        _activeRecordEventCount = 3; //capped at 3

        //Get the active map view
        var mapView = MapView.Active;
        if (mapView?.Map == null)
          return;

        //Get the fabric
        var fabricLyr = mapView.Map.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
        if (fabricLyr == null)
          return;

        var theNewActiveRecord = e.IncomingActiveRecord;
        if (theNewActiveRecord == null)
          return;

        //get the Records layer
        var recordsLyr = fabricLyr.GetRecordsLayerAsync().Result.FirstOrDefault();
        if (recordsLyr == null)
          return;

        List<long> lst = new();
        lst.Add(theNewActiveRecord.ObjectID);
        var QueryFilter = new QueryFilter { ObjectIDs = lst.ToArray() };
        var cim_g2g = await mapView.Map.GetGroundToGridCorrection();
        if (cim_g2g == null)
          cim_g2g = new CIMGroundToGridCorrection(); // CIM for ground to grid is null for new maps, so initialize it for the first time here.

        object oScaleFactor = null;
        object oDirectionOffset = null;
        bool bHasChanges = false;
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
              var scaleFactor = Convert.ToDouble(oScaleFactor);
              if (!bHasChanges)
                bHasChanges = cim_g2g.ConstantScaleFactor != scaleFactor;
              cim_g2g.ConstantScaleFactor = scaleFactor;
              if(!bHasChanges)
                bHasChanges = cim_g2g.Enabled = false;
              cim_g2g.Enabled = true;
              if (!bHasChanges)
                bHasChanges = cim_g2g.UseScale = false;
              cim_g2g.UseScale = true;
              if (!bHasChanges)
                bHasChanges = cim_g2g.ScaleType != GroundToGridScaleType.ConstantFactor;
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
              var directionOffset = Convert.ToDouble(oDirectionOffset);
              if (!bHasChanges)
                bHasChanges = cim_g2g.Direction != directionOffset;
              cim_g2g.Direction = directionOffset;
              // store and set this in decimal degrees, irrespective of project unit settings
              if (!bHasChanges)
                bHasChanges = cim_g2g.Enabled = false;
              cim_g2g.Enabled = true;
              if (!bHasChanges)
                bHasChanges = cim_g2g.UseDirection = false;
              cim_g2g.UseDirection = true;
            }
            else
              cim_g2g.UseDirection = false;
          }
        }
        if (oScaleFactor == null && oDirectionOffset == null)
          return;//if both direction offset and scale are null, do nothing

        if (!bHasChanges)
          return; //if there was no change to anything do nothing (don't add an operation to the undo stack)

        await mapView.Map.SetGroundToGridCorrection(cim_g2g);
      }
      catch
      { 
        return; 
      }
    }

    private void ProjectOpenedEventMethod(ProjectEventArgs e)
    {
      _activeRecordEventCount = 0;
    }

    private void MapInitializeEventMethod(MapViewEventArgs e)
    {
      _activeRecordEventCount = 0;//reset to zero
    }

    private void ActiveMapViewChangedEventMethod(ActiveMapViewChangedEventArgs e)
    {
      try
      {
        QueuedTask.Run(() =>
        { //Get the active map view
          var mapView = e.IncomingView;
          if (mapView?.Map == null)
            return;

          //Get the fabric
          var fabricLyr = mapView.Map.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
          if (fabricLyr == null)
            return;

          CIMParcelLayer cimParcelLayer = (CIMParcelLayer)fabricLyr.GetDefinition();
          var cimActiveRecord = cimParcelLayer.ParcelFabricActiveRecord;
          var storedActiveRecord = cimActiveRecord.ActiveRecord;

          #region 3.2 and 3.3 workaround, with future-proofing (for fix applied in 3.4 and higher)
          _mapOpenExpectedEventCount = 0;
          var vers = GetProVersion();
          if (vers == "3.2" || vers == "3.3")
          {
            if (cimActiveRecord.Enabled && storedActiveRecord != null)
              _mapOpenExpectedEventCount = 1;

            if (!cimActiveRecord.Enabled && storedActiveRecord != null)
              _mapOpenExpectedEventCount = 2;
          }
          else
          {
            if (cimActiveRecord.Enabled)
              _mapOpenExpectedEventCount = 1;
          }

          #endregion
        });
      }
      catch
      {
        return;
      }
    }

    private static string GetProVersion()
    {
      System.Reflection.Assembly assembly = System.Reflection.Assembly.GetEntryAssembly();
      FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
      return $"{fileVersionInfo.ProductMajorPart}.{fileVersionInfo.ProductMinorPart}";
    }

    protected override bool Initialize() //Called when the Module is initialized.
    {
      ProjectOpenedEvent.Subscribe(ProjectOpenedEventMethod);
      MapViewInitializedEvent.Subscribe(MapInitializeEventMethod);
      ActiveMapViewChangedEvent.Subscribe(ActiveMapViewChangedEventMethod);
      ActiveParcelRecordChangingEvent.Subscribe(ActiveRecordEventMethod);
      return base.Initialize();
    }

    protected override void Uninitialize() 
    {
      ProjectOpenedEvent.Unsubscribe(ProjectOpenedEventMethod);
      MapViewInitializedEvent.Unsubscribe(MapInitializeEventMethod);
      ActiveMapViewChangedEvent.Unsubscribe(ActiveMapViewChangedEventMethod);
      ActiveParcelRecordChangingEvent.Unsubscribe(ActiveRecordEventMethod);
      return;
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
