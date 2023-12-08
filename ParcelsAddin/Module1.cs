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
using ArcGIS.Desktop.Mapping.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ParcelsAddin
{
  internal class Module1 : Module
  {
    private static Module1 _this = null;
    private bool _hasParcelSelection = false;
    internal Module1()
    {
      //this code is used for tool enablement, based on parcel selection
      //but is commented out as it is not used currently.
      //(performance related reason. Fix TBD.)

      //var commandId = "esri_editing_selectParcelFeaturesButton";

      //if (FrameworkApplication.GetPlugInWrapper(commandId) is ICommand iCommand)
      //  _hasParcelSelection = iCommand.CanExecute(null);

      //MapSelectionChangedEvent.Subscribe(MapSelectionChangeEventMethod);
    }

    ~Module1()
    {
      //this code is used for tool enablement, based on parcel selection
      //but is commented out as it is not used currently.
      //(performance related reason. Fix TBD.)

      //MapSelectionChangedEvent.Unsubscribe(MapSelectionChangeEventMethod);
    }

    /// <summary>
    /// Retrieve the singleton instance to this module here
    /// </summary>
    public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("ParcelsAddin_Module");

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

    internal bool HasParcelPolygonSelection
    {
      get
      {
        return _hasParcelSelection;
      }
    }

    private void MapSelectionChangeEventMethod(MapSelectionChangedEventArgs args)
    {
      var sel = args.Selection;
      var selDict = sel.ToDictionary<FeatureLayer>();
      var polygonLayers = selDict.Keys.Where(fl => fl.ShapeType == esriGeometryType.esriGeometryPolygon);

      // if there are parcel polygon layers in the selection, set the flag to true.
      _hasParcelSelection = polygonLayers.Any();
    }
  }
}
