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

namespace CurvesAndLines
{
  /// <summary>
  /// Interaction logic for ConfigureSimplifyByTangentDialog.xaml
  /// </summary>
  public partial class ConfigureSimplifyByTangentDialog : ArcGIS.Desktop.Framework.Controls.ProWindow
  {
    private static readonly Regex _regex = new("[^0-9.,-]+"); //regex that matches valid text
    public ConfigureSimplifyByTangentDialog()
    {
      InitializeComponent();
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
