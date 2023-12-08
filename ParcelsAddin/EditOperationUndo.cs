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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParcelsAddin
{
  internal class EditOperationUndo : Button
  {
    protected override async void OnClick()
    {
      var opManager = MapView.Active.Map.OperationManager;
      if (opManager != null)
      {
        // undo an edit operation
        if (opManager.CanUndo)
          await opManager.UndoAsync(1,"Editing");
      }
    }
    protected override void OnUpdate()
    {
      var opManager = MapView.Active?.Map?.OperationManager;
      List<Operation> ops = new();
      bool enableSate = false; //criteria enabled state  
      bool criteria = false;  //criteria for disabledText  

      if (opManager != null)
      {
        // find all the undo operations for the Editing category
        ops = opManager.FindUndoOperations(o => o.Category == "Editing");
        enableSate = ops.Count > 0; //criteria enabled state  
        criteria = ops.Count == 0;  //criteria for disabledText  
      }

      if (enableSate)
      {
        this.Enabled = true;  //tool is enabled  
        this.Tooltip = "Undo " + ops[0].Name;
      }
      else
      {
        this.Enabled = false;  //tool is disabled  
                               //customize your disabledText here  
        if (criteria)
          this.DisabledTooltip = "No edits to undo.";
      }
    }
  }
}
