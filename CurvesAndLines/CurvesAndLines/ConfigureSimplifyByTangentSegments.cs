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
using ArcGIS.Desktop.Core.UnitFormats;

namespace CurvesAndLines
{
  internal class ConfigureSimplifyByTangentSegments : Button
  {
    protected override async void OnClick()
    {
      string distUnitAbbreviation = "m";
      double metersPerBackstageUnit = 1.0;
      int iRounding = 5;
      //get the distance unit and format from the backstage default settings
      await QueuedTask.Run(() =>
      {
        var backstageDistanceUnit =
          DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance);
        distUnitAbbreviation = backstageDistanceUnit.Abbreviation;
        metersPerBackstageUnit = backstageDistanceUnit.MeasurementUnit.ConversionFactor;

        var dispUnitFormat = backstageDistanceUnit.UnitFormat as CIMNumericFormatBase;
        if (dispUnitFormat.RoundingOption == esriRoundingOptionEnum.esriRoundNumberOfDecimals)
          iRounding = dispUnitFormat.RoundingValue;

        if (iRounding < 3)
          iRounding = 3;
        string sFormat = new('0', iRounding);
        sFormat = "0." + sFormat;
      });

      ConfigureSimplifyByTangentViewModel _VM = new(metersPerBackstageUnit, iRounding, distUnitAbbreviation);
      var ConfigureSimplifyByTangentDialog = new ConfigureSimplifyByTangentDialog
      {
        Owner = FrameworkApplication.Current.MainWindow,
        DataContext = _VM
      };

      if (ConfigureSimplifyByTangentDialog.ShowDialog() == true)
      {
        #region Collect parameters from dialog and save to settings

        string maxAllowableOffsetMetric =
          _VM.ConfigureSimplifyByTangentModel.MaxAllowableOffsetInBackstageUnits.ToString();

        //convert to metric 
        if (Double.TryParse(maxAllowableOffsetMetric, out double maxAllowableOffset))
        {
          var dMetricMaxAllowableOffset = maxAllowableOffset * metersPerBackstageUnit;
          if (dMetricMaxAllowableOffset < 0.001)
            dMetricMaxAllowableOffset = 0.001;
          maxAllowableOffsetMetric = dMetricMaxAllowableOffset.ToString("F5");
        }
        ConfigurationsLastUsed.Default["ConfigureSimplifyByTangentLastUsedParams"] =
           maxAllowableOffsetMetric + "|";
        ConfigurationsLastUsed.Default.Save();//comment out if you only want to save settings within each app session
        #endregion Collect parameters from dialog and save to settings
      }

    }
  }
}
