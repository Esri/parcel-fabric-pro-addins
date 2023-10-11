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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ParcelsAddin
{
  /// <summary>
  /// Interaction logic for ConfigureUpdateCOGODialog.xaml
  /// </summary>
  public partial class ConfigureUpdateCOGODialog : ArcGIS.Desktop.Framework.Controls.ProWindow
  {
    private static readonly Regex _regex = new ("[^0-9.,-]+"); //regex that matches valid text
    public ConfigureUpdateCOGODialog()
    {
      InitializeComponent();
      //initialize controls as disabled, and downstream
      //code-behind logic enables controls as needed

      optAllDistances.IsEnabled = false;
      optDistanceTolerances.IsEnabled = false;
      optNullDistancesOnly.IsEnabled = false;
      txtDistanceDifferenceTolerance.IsEnabled = false;
      lblDistanceTolerance.Opacity = 0.5;
      lblDistanceDifferenceToleranceUnit.Opacity = 0.5;
      
      optAllDirections.IsEnabled = false;
      optDirectionTolerances.IsEnabled = false;
      optNullDirectionsOnly.IsEnabled = false;
      txtDirectionDifferenceTolerance.IsEnabled = false;
      txtDirectionLateralOffsetTolerance.IsEnabled = false;
      lblDirectionDiffTolerance.Opacity = 0.5;
      lblAngleUnits.Opacity = 0.5;
      lblLateralOffsetTolerance.Opacity = 0.5;
      lblLateralOffsetToleranceUnit.Opacity = 0.5;
    }

    private void CheckBox_Checked(object sender, RoutedEventArgs e)
    {
      if ((sender as CheckBox).Name == nameof(chkUpdateDistance))
      {
        optAllDistances.IsEnabled = optNullDistancesOnly.IsEnabled = 
        optDistanceTolerances.IsEnabled = (bool)(sender as CheckBox).IsChecked;

        txtDistanceDifferenceTolerance.IsEnabled = (bool)(sender as CheckBox).IsChecked;

       lblDistanceDifferenceToleranceUnit.Opacity = lblDistanceTolerance.Opacity = 
          (bool)(sender as CheckBox).IsChecked? 1.0:0.5;
      }
      else if ((sender as CheckBox).Name == nameof(chkUpdateDirection))
      {
        optAllDirections.IsEnabled = optNullDirectionsOnly.IsEnabled =
        optDirectionTolerances.IsEnabled = (bool)(sender as CheckBox).IsChecked;

        txtDirectionDifferenceTolerance.IsEnabled = txtDirectionLateralOffsetTolerance.IsEnabled =
          (bool)(sender as CheckBox).IsChecked;

        lblDirectionDiffTolerance.Opacity = lblAngleUnits.Opacity =
        lblLateralOffsetTolerance.Opacity = lblLateralOffsetToleranceUnit.Opacity =
        (bool)(sender as CheckBox).IsChecked ? 1.0 : 0.5;
      }

      btnOK.IsEnabled = (bool)chkUpdateDirection.IsChecked || (bool)chkUpdateDistance.IsChecked;

      e.Handled = true;
    }

    private void TextBox_GotFocus(object sender, RoutedEventArgs e)
    {
      TextBox textBox = (TextBox)sender;
      textBox.Dispatcher.BeginInvoke(new Action(() => textBox.SelectAll()));
      e.Handled = true;
    }

    private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
      e.Handled = IsTextValid(e.Text);
    }

    private static bool IsTextValid(string text)
    {
      return _regex.IsMatch(text);
    }

    private void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
      if (e.DataObject.GetDataPresent(typeof(string)))
      { 
      string text = (string)e.DataObject.GetData(typeof(string));
        if (!IsTextValid(text))
         e.CancelCommand(); 
      }
      else
        e.CancelCommand();
    }
  }
}
