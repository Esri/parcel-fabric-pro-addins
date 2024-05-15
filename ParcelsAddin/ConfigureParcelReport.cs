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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace ParcelsAddin
{
	internal class ConfigureParcelReport : Button
	{
    private readonly ConfigureParcelReportViewModel _VM = new();
    protected override void OnClick()
    {
      #region Collect parameters from dialog and save to settings
      ConfigureParcelReportDialog ConfigureParcelReportInput = new ();
      ConfigureParcelReportInput.Owner = FrameworkApplication.Current.MainWindow;
      ConfigureParcelReportInput.DataContext = _VM;

      if (ConfigureParcelReportInput.ShowDialog() == true)
      {
        string dirTypeName = _VM.ConfigureParcelReportModel.DirectionTypeName;
        ArcGIS.Core.SystemCore.DirectionType directionType;
        string directionTypeCodeAsString = "";
        if (dirTypeName.ToLower() != "<project units>")
        {
          directionType =
              _VM.ConfigureParcelReportModel.DirectionTypeLookup[dirTypeName];
          _VM.ConfigureParcelReportModel.DirectionType = directionType;
          directionTypeCodeAsString = ((int)directionType).ToString();

        }
        //"<Project Units>|directiontypecode|<Dataset Units>|Chord|Radius And Arclength|Symbol [dd°mm'ss"]|Comma-separated"

        string sTextFormatStyle = _VM.ConfigureParcelReportModel.TextFormatStyle;
        ConfigurationsLastUsed.Default["ConfigureParcelReportLastUsedParams"] =
          _VM.ConfigureParcelReportModel.DirectionTypeName
          + "|" + directionTypeCodeAsString
          + "|" + _VM.ConfigureParcelReportModel.DistanceUnitName
          + "|" + _VM.ConfigureParcelReportModel.CircularArcDirectionParameter
          + "|Radius And Arclength"
          + "|" + _VM.ConfigureParcelReportModel.DirectionSymbol
          + "|" + sTextFormatStyle;

        ConfigurationsLastUsed.Default.Save();//comment out if you only want to save settings within each app session
      }
      #endregion Collect parameters from dialog and save to settings
    }
  }
}
