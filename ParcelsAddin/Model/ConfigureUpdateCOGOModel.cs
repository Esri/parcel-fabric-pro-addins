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
  internal class ConfigureUpdateCOGOModel : INotifyPropertyChanged
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
    private double _backstageMetersPerDistanceUnit;
    public List<string> SpatialReferenceSourceList { get; set; }
    public string SpatialReferenceSource { get; set; }
    public string DistanceUnitAbbreviation { get; set; }
    public double MetersPerDataSourceLinearUnit { get; set; }
    public double MetersPerBackstageDistanceUnit { get; set; } 
    public bool UpdateDistances { get; set; }
    public bool[] UpdateDistancesOption { get; set; }
    public string DistanceDifferenceToleranceInBackstageUnits { get; set; }
    public double DistanceDifferenceToleranceInMeters { get; set; }
    public bool UpdateDirections { get; set; }
    public bool[] UpdateDirectionsOption { get; set; }
    public string DifferenceDirectionToleranceSeconds { get; set; }
    public string LateralOffsetToleranceInBackstageUnits { get; set; }
    public double LateralOffsetToleranceInMeters { get; set; }
    public double DifferenceDirectionToleranceDecimalDegrees { get; set; }
  }
}
