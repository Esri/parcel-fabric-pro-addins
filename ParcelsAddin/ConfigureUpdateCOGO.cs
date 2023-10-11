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
using ArcGIS.Desktop.Core.UnitFormats;

namespace ParcelsAddin
{
  internal class ConfigureUpdateCOGO : Button
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
        string sFormat = new ('0', iRounding);
        sFormat = "0." + sFormat;
      });

      ConfigureUpdateCOGOViewModel _VM = new(metersPerBackstageUnit, iRounding, distUnitAbbreviation);

      var ConfigureUpdateCOGODialog = new ConfigureUpdateCOGODialog
      {
        Owner = FrameworkApplication.Current.MainWindow,
        DataContext = _VM
      };

      if (ConfigureUpdateCOGODialog.ShowDialog() == true)
      {
        #region Collect parameters from dialog and save to settings
        string srSrc = _VM.ConfigureUpdateCOGOModel.SpatialReferenceSource;
        string upDtDist1 = _VM.ConfigureUpdateCOGOModel.UpdateDistances.ToString();
        string optDistUpdateAll2 = _VM.ConfigureUpdateCOGOModel.UpdateDistancesOption[0].ToString();
        string optDistUpdateOnlyNull3 = _VM.ConfigureUpdateCOGOModel.UpdateDistancesOption[1].ToString();
        string optDistUpdateByDiffTol4 =
          _VM.ConfigureUpdateCOGOModel.UpdateDistancesOption[2].ToString();
        string distDiffTolMetric5 = _VM.ConfigureUpdateCOGOModel.DistanceDifferenceToleranceInBackstageUnits;

        //convert to metric 
        if (Double.TryParse(distDiffTolMetric5, out double distDiffTolNew))
        {
          var dMetricDiffTolerance = distDiffTolNew * metersPerBackstageUnit;
          if (dMetricDiffTolerance < 0.001)
            dMetricDiffTolerance = 0.001;
          distDiffTolMetric5 = dMetricDiffTolerance.ToString("F4");
        }
        string upDtDir6 = _VM.ConfigureUpdateCOGOModel.UpdateDirections.ToString();
        string optDirUpdateAll7 = _VM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[0].ToString();
        string optDirUpdateOnlyNull8 = _VM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[1].ToString();
        string optDirUpdateByDiffTol9 =
          _VM.ConfigureUpdateCOGOModel.UpdateDirectionsOption[2].ToString();
        string dirDiffTolSeconds10 =
          _VM.ConfigureUpdateCOGOModel.DifferenceDirectionToleranceSeconds.ToString();
        string latOffsetTolMetric11 =
          _VM.ConfigureUpdateCOGOModel.LateralOffsetToleranceInBackstageUnits.ToString();

        //convert to metric 
        if (Double.TryParse(latOffsetTolMetric11, out double lateralOffsetTol))
        {
          var dMetricLateralOffsetTolerance = lateralOffsetTol * metersPerBackstageUnit;
          if (dMetricLateralOffsetTolerance < 0.001)
            dMetricLateralOffsetTolerance = 0.001;
          latOffsetTolMetric11 = dMetricLateralOffsetTolerance.ToString("F4");
        }
        ConfigurationsLastUsed.Default["ConfigureUpdateCOGOLastUsedParams"] = 
          srSrc + "|" + upDtDist1 + "|" + optDistUpdateAll2 + "|" + optDistUpdateOnlyNull3 + "|" +
          optDistUpdateByDiffTol4 + "|" + distDiffTolMetric5 + "|" + upDtDir6 + "|" + optDirUpdateAll7 + "|" + optDirUpdateOnlyNull8 + "|" + 
          optDirUpdateByDiffTol9 + "|" + dirDiffTolSeconds10 + "|" + latOffsetTolMetric11;
        ConfigurationsLastUsed.Default.Save();//comment out if you only want to save settings within each app session
        #endregion Collect parameters from dialog and save to settings
      }
    }
  }
}
