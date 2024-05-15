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
using ArcGIS.Core.SystemCore;

namespace ParcelsAddin
{
  internal class ConfigureParcelReportViewModel : PropertyChangedBase
  {
    private ConfigureParcelReportModel _ConfigureParcelReportModel;
    
	  private readonly string _directionTypeName;
	  private readonly List<string> _directionTypesNameList;
	  private readonly DirectionType _directionType;
    private readonly Dictionary<string, DirectionType> _dictDirectionTypeLookup;

    private readonly string _distanceUnitName;
    private readonly List<string> _distanceUnitsNameList;

    private readonly string _circularArcDirectionParameter;
    private readonly List<string> _circularArcDirectionParameterList;

    private readonly string _directionSymbol;
    private readonly List<string> _directionSymbolList;

    private readonly string _textFormatStyle;
    private readonly List<string> _textFormatStyleList;

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

    public ConfigureParcelReportViewModel()
    {
      _directionTypesNameList = new List<string> { "<Project Units>", "Quadrant Bearing", "North Azimuth", "South Azimuth", "Polar" };
      _dictDirectionTypeLookup = new();
      _dictDirectionTypeLookup.Add("Quadrant Bearing", DirectionType.QuadrantBearing);
      _dictDirectionTypeLookup.Add("North Azimuth", DirectionType.NorthAzimuth);
      _dictDirectionTypeLookup.Add("South Azimuth", DirectionType.SouthAzimuth);
      _dictDirectionTypeLookup.Add("Polar", DirectionType.Polar);

      _directionTypeName = "Quadrant Bearing";
      _directionType = DirectionType.QuadrantBearing;

      _distanceUnitsNameList = new List<string> { "<Dataset Units>", "<Project Units>", "Meters", "Feet", "US Feet", "Chains" };

      _circularArcDirectionParameterList = new List<string> { "Chord", "Tangent", "Radial" };

      _directionSymbolList = new List<string> { "Symbols [dd°mm'ss\"]" , "Dashes [dd-mm-ss]", "Spaces [dd mm ss]" };

      _textFormatStyleList = new List<string> { "Comma-separated", "Columns" };

      string sParamString = ConfigurationsLastUsed.Default["ConfigureParcelReportLastUsedParams"] as string;
      string[] sParams = sParamString.Split('|');

      if (sParams.Length == 0)
      {
        _directionTypeName = "<Project Units>";
        _directionType = DirectionType.Polar;
		    _distanceUnitName = "<Dataset Units>";

        _circularArcDirectionParameter = "Chord";
        _directionSymbol = "Symbols [dd°mm'ss\"]";
        _textFormatStyle = "Comma-separated";

      }
      else
      {
        try
        {
          _directionTypeName = sParams[0];
          if (String.IsNullOrEmpty(_directionTypeName))
            _directionTypeName = "<Project Units>";
        }
        catch { _directionTypeName = "<Project Units>"; }
		
        try
        {
          _distanceUnitName = sParams[2];
          if (String.IsNullOrEmpty(_distanceUnitName))
            _distanceUnitName = "<Dataset Units>";
        }
        catch { _distanceUnitName = "<Dataset Units>"; }

        try
        {
          _circularArcDirectionParameter = sParams[3];
          if (String.IsNullOrEmpty(_circularArcDirectionParameter))
            _circularArcDirectionParameter = "Chord";
        }
        catch { _circularArcDirectionParameter = "Chord"; }

        try
        {
          _directionSymbol = sParams[5];
          if (String.IsNullOrEmpty(_directionSymbol))
            _directionSymbol = "Symbols [dd°mm'ss\"]";
        }
        catch { _directionSymbol = "Symbols [dd°mm'ss\"]"; }

        try
        {
          _textFormatStyle = sParams[6];
          if (String.IsNullOrEmpty(_textFormatStyle))
            _textFormatStyle = "Comma-separated";
        }
        catch { _textFormatStyle = "Comma-separated"; }

      }

      try
      {
        _directionType = _dictDirectionTypeLookup[_directionTypeName];
      }catch { _directionTypeName = "Quadrant Bearing"; }

      _ConfigureParcelReportModel = new ConfigureParcelReportModel
      {
        DirectionType = _directionType,
        DirectionTypeName = _directionTypeName,
        DirectionTypesNameList = _directionTypesNameList,
        DirectionTypeLookup = _dictDirectionTypeLookup,
		    DistanceUnitName = _distanceUnitName,
        DistanceUnitsNameList = _distanceUnitsNameList,
        CircularArcDirectionParameter = _circularArcDirectionParameter,
        CircularArcDirectionParameterList = _circularArcDirectionParameterList,
        DirectionSymbol = _directionSymbol,
        DirectionSymbolList = _directionSymbolList,
        TextFormatStyle = _textFormatStyle,
        TextFormatStyleList= _textFormatStyleList
      };

    }
    public ConfigureParcelReportModel ConfigureParcelReportModel
    {
      get { return _ConfigureParcelReportModel; }
      set { _ConfigureParcelReportModel = value; }
    }

  }
}
