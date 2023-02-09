/*   Copyright 2023 Esri
   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.
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

namespace ParcelsAddin
{
  internal class ConfigureAreaUnitsViewModel : PropertyChangedBase
  {
    private ConfigureAreaUnitsModel _ConfigureAreaUnitsModel;
    private string _areaUnitName;
    private string _areaValueText;
    private List<string> _areaUnitsNameList;
    private Dictionary<string, long> _dictAreaUnitsCodeLookup; // = new Dictionary<string, long>();
    private double _largeParcelAreaInSquareMeters;
    private long _largeParcelAreaCode;

    public ICommand OKCommand
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

    public ConfigureAreaUnitsViewModel()
    {
      _areaUnitsNameList = new List<string> { "Acres", "Hectares", "Square Meters", "Square Feet" };
      _dictAreaUnitsCodeLookup = new Dictionary<string, long>();
      _dictAreaUnitsCodeLookup.Add("Hectares", 109401);
      _dictAreaUnitsCodeLookup.Add("Acres", 109402);
      _dictAreaUnitsCodeLookup.Add("Square Meters", 109404);
      _dictAreaUnitsCodeLookup.Add("Square Feet", 109405);

      _areaUnitName = "Acres";
      _largeParcelAreaCode = 109402;
      _largeParcelAreaInSquareMeters = 1011.715;//1/4 Acre default large parcel


      string sParamString = ConfigureAreaUnitsDlg.Default["LastUsedParams"] as string;
      string[] sParams = sParamString.Split('|'); //"Acres|0.25"
      if (sParams.Length == 0)
      {
        _areaUnitName = "Acres";
        _areaValueText = "0.25";
        _largeParcelAreaInSquareMeters = 1011.715;// 1/4 Acre default large parcel
        _largeParcelAreaCode = 109402;
      }
      else
      {
        try
        {
          _areaUnitName = sParams[0];
          if (String.IsNullOrEmpty(_areaUnitName))
            _areaUnitName = "Acres";
        }
        catch { _areaUnitName = "Acres"; }
        try
        {
          _areaValueText= sParams[1];
          if (String.IsNullOrEmpty(_areaValueText))
            _areaValueText = "0.25";
        }
        catch { _areaValueText = "0.25"; }
      }

      _largeParcelAreaCode = _dictAreaUnitsCodeLookup[_areaUnitName];

      _ConfigureAreaUnitsModel = new ConfigureAreaUnitsModel
      {
        AreaUnitName = _areaUnitName,
        AreaUnitsNameList = _areaUnitsNameList,
        LargeAreaValueText = _areaValueText,
        LargeParcelAreaUnitCode = _largeParcelAreaCode,
        AreaUnitsCodeLookup = _dictAreaUnitsCodeLookup
      };

    }
    public ConfigureAreaUnitsModel ConfigureAreaUnitsModel
    {
      get { return _ConfigureAreaUnitsModel; }
      set { _ConfigureAreaUnitsModel = value; }
    }

  }
}
