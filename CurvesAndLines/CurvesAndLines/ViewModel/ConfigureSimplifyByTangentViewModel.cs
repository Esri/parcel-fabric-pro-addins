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
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Controls;
using ArcGIS.Desktop.Core.UnitFormats;

namespace CurvesAndLines
{
  internal class ConfigureSimplifyByTangentViewModel : PropertyChangedBase
  {
    private ConfigureSimplifyByTangentModel _ConfigureSimplifyByTangentModel;
    
    private readonly string _distanceUnitAbbr = "m";

    private readonly string _maxAllowableOffsetInBackstageUnits;
    private readonly string _maxAllowableOffsetInMeters;
    private readonly double _maxAllowableOffsetInMetersNumeric;

    public static ICommand OKCommand
    {
      get
      {
        return new RelayCommand((dlgParam) =>
        {
          ProWindow param = dlgParam as ProWindow;
          param.DialogResult = true;
        }, () => true);
      }
    }

    public ConfigureSimplifyByTangentViewModel(double metersPerBackstageDistanceUnit,
      int DistanceDisplayPrecision = 3, string DistanceUnitAbbreviation = "m")
    {
      _distanceUnitAbbr = DistanceUnitAbbreviation;

      _maxAllowableOffsetInBackstageUnits = "2.000";
      _maxAllowableOffsetInMeters = "2.000";

      string sParamString = ConfigurationsLastUsed.Default["ConfigureSimplifyByTangentLastUsedParams"] as string;
      string[] sParams = sParamString.Split('|');
      if (sParams.Length == 0)
      {
        _distanceUnitAbbr = DistanceUnitAbbreviation;
        _maxAllowableOffsetInBackstageUnits = "2.000";
        _maxAllowableOffsetInMeters = "2.000";
      }
      else
      {
        try
        {
          _maxAllowableOffsetInMeters = sParams[0];
          if (String.IsNullOrEmpty(_maxAllowableOffsetInMeters))
            _maxAllowableOffsetInMeters = "2.000";
        }
        catch { _maxAllowableOffsetInMeters = "2.000"; }
      }

      //Convert units for UI 

      if (!Double.TryParse(_maxAllowableOffsetInMeters, out _maxAllowableOffsetInMetersNumeric))
        _maxAllowableOffsetInMetersNumeric = 2.000;

      string sFormat = new('0', DistanceDisplayPrecision);
      sFormat = "0." + sFormat;
      _maxAllowableOffsetInBackstageUnits =
        (_maxAllowableOffsetInMetersNumeric / metersPerBackstageDistanceUnit).ToString(sFormat);

      _ConfigureSimplifyByTangentModel = new ConfigureSimplifyByTangentModel
      { 
        DistanceUnitAbbreviation = _distanceUnitAbbr,
        MaxAllowableOffsetInBackstageUnits = _maxAllowableOffsetInBackstageUnits,
        MaxAllowableOffsetToleranceInMeters = _maxAllowableOffsetInMetersNumeric
      };

    }
    public ConfigureSimplifyByTangentModel ConfigureSimplifyByTangentModel
    {
      get { return _ConfigureSimplifyByTangentModel; }
      set { _ConfigureSimplifyByTangentModel = value; }
    }

  }
}
