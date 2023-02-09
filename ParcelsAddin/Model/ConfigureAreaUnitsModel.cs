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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
namespace ParcelsAddin
{
  public class ConfigureAreaUnitsModel : INotifyPropertyChanged
  {
    #region INotifyPropertyChanged Members  

    public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyPropertyChanged(string propertyName)
    {
      if (PropertyChanged != null)
      {
        PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
      }
    }
    #endregion

    private string _areaUnitName;
    private List<string> _areaUnitsNameList;
    private List<long> _areaUnitsCodeList;
    private Dictionary<string, long> _dictAreaUnitCodeLookup; // = new Dictionary<string, long>();

    private double _largeParcelAreaInSquareMeters;//1 acre = 4046.86 sq.meters
    private long _largeParcelAreaUnitCode;
    private string _largeAreaValueText;
    private double _sqMetersPerAreaUnit;

    // 109402); // use acre
    // 109405); //use square foot
    // 109404); //use square meter
    public string AreaUnitName
    {
      get
      {
        return _areaUnitName;
      }
      set
      {
        _areaUnitName = value;
        SyncAreaProperties();
        NotifyPropertyChanged(nameof(AreaUnitName));
      } //use nameof to avoid hard coding strings.
    }

    public string LargeAreaValueText
    {
      get
      {
        return _largeAreaValueText;
      }
      set
      {
        _largeAreaValueText = value;
        SyncAreaProperties();
        NotifyPropertyChanged(nameof(LargeAreaValueText)); 
      } //use nameof to avoid hard coding strings.
    }

    public long LargeParcelAreaUnitCode
    {
      get
      {
        return _largeParcelAreaUnitCode;
      }
      set
      {
        _largeParcelAreaUnitCode = value;
        SyncAreaProperties();
        NotifyPropertyChanged(nameof(LargeParcelAreaUnitCode)); 
      } //use nameof to avoid hard coding strings.
    }

    public double LargeParcelAreaInSquareMeters
    {
      get
      {
        return _largeParcelAreaInSquareMeters;
      }
    }

    public double SquareMetersPerAreaUnit
    {
      get
      {
        return _sqMetersPerAreaUnit;
      }
    }

    public List<string> AreaUnitsNameList
    {
      get
      {
        return _areaUnitsNameList;
      }
      set
      {
        _areaUnitsNameList = value;
        NotifyPropertyChanged(nameof(AreaUnitsNameList));
      }
    }

    public List<long> AreaUnitsCodeList
    {
      get
      {
        return _areaUnitsCodeList;
      }
      set
      {
        _areaUnitsCodeList = value;
        NotifyPropertyChanged(nameof(AreaUnitsCodeList));
      }
    }

    public Dictionary<string, long> AreaUnitsCodeLookup
    {
      get
      {
        return _dictAreaUnitCodeLookup;
      }
      set
      {
        _dictAreaUnitCodeLookup = value;
        NotifyPropertyChanged(nameof(AreaUnitsCodeLookup));
      }
    }

    private void SyncAreaProperties()
    {
      //compute and set the metric value for the large parcel area
      double largeParcelAreaValue = 0.0;

      if (!Double.TryParse(_largeAreaValueText, out largeParcelAreaValue))
      {
        largeParcelAreaValue = 0.25;
        _largeParcelAreaInSquareMeters = 4046.86 * largeParcelAreaValue; //default to 1/4 acre
        _sqMetersPerAreaUnit = 4046.86;
        return;
      }
      if (_largeParcelAreaUnitCode == 109402)
      {
        _largeParcelAreaInSquareMeters = 4046.86 * largeParcelAreaValue; //1 acre = 4046.86 sq.m
        _sqMetersPerAreaUnit = 4046.86;
      }
      else if (_largeParcelAreaUnitCode == 109401)
      {
        _largeParcelAreaInSquareMeters = 10000.0 * largeParcelAreaValue; //1 hectare = 10000 sq.m
        _sqMetersPerAreaUnit = 10000.0;
      }
      else if (_largeParcelAreaUnitCode == 109405)
      {
        _largeParcelAreaInSquareMeters = 0.0929 * largeParcelAreaValue; //1 sq.ft = 0.0929 sq.m
        _sqMetersPerAreaUnit = 0.0929;
      }
      else if (_largeParcelAreaUnitCode == 109404)
      {
        _largeParcelAreaInSquareMeters = 1.0 * largeParcelAreaValue; //1 sq.m = 1 sq.m
        _sqMetersPerAreaUnit = 1.0;
      }
    }
  }
}
