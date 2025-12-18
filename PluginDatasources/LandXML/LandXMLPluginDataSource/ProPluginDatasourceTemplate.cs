/*
   Copyright 2025 Esri

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       https://www.apache.org/licenses/LICENSE-2.0

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
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Geometry;
using Microsoft.VisualBasic.FileIO;
using ArcGIS.Core.Hosting.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using ArcGIS.Core.Data.Exceptions;
using ArcGIS.Core.Internal.CIM;
using System.IO;
using System.Windows.Shapes;
using System.Collections;

namespace LandXMLPluginDataSource
{
  /// <summary>
  /// Implements a custom plugin datasource for reading land xml files
  /// </summary>
  /// <remarks>A per thread instance will be created (as needed) by Pro.</remarks>
  public class ProPluginDatasourceTemplate : PluginDatasourceTemplate
  {

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    private string _filePath = "";
    private uint _thread_id;
    private ArcGIS.Core.Geometry.SpatialReference _spatialReference = SpatialReferences.WGS84;

    private Dictionary<string, PluginTableTemplate> _tables;

    /// <summary>
    /// Open the specified workspace
    /// </summary>
    /// <param name="connectionPath">The path to the workspace</param>
    /// <remarks>
    /// .NET Clients access Open via the ArcGIS.Core.Data.PluginDatastore.PluginDatastore class
    /// whereas Native clients (Pro internals) access via IWorkspaceFactory</remarks>
    public override void Open(Uri connectionPath)
    {


      var localPath = connectionPath.LocalPath;
      if (!System.IO.Path.GetExtension(localPath).Equals(".lxml", StringComparison.CurrentCultureIgnoreCase))
      {
        localPath = System.IO.Directory.GetParent(localPath).FullName;
      }
      if (!(System.IO.File.Exists(localPath) || System.IO.Directory.Exists(localPath)))
      {
        throw new System.IO.DirectoryNotFoundException(connectionPath.LocalPath);
      }



      //////if (!System.IO.Directory.Exists(connectionPath.LocalPath))
      //////{
      //////  throw new System.IO.DirectoryNotFoundException(connectionPath.LocalPath);
      //////}
      //initialize
      //Strictly speaking, tracking your thread id is only necessary if
      //your implementation uses internals that have thread affinity.
      _thread_id = GetCurrentThreadId();

      _tables = new Dictionary<string, PluginTableTemplate>();
      _filePath = connectionPath.LocalPath;

      //confirm the projection file is present
      // Get the files with the specified extension
      string fileExtension = "*.prj";

      string[] files;

      //if (!System.IO.File.Exists(_filePath))
      files = Directory.GetFiles(_filePath, fileExtension);

      // Count the files
      int fileCount = files.Length;
      if (fileCount == 1)
      {//if there is more than one projection file, opt out and use default sr
        var prjFileName =
          Directory.GetFiles(_filePath, fileExtension, System.IO.SearchOption.TopDirectoryOnly).First();
        prjFileName = System.IO.Path.Combine(_filePath, prjFileName);
        StreamReader srPRJ = new(prjFileName);
        string sWkt = srPRJ.ReadLine();
        srPRJ.Close();
        var pSpatRefBuilder = new SpatialReferenceBuilder(sWkt);
        _spatialReference = pSpatRefBuilder.ToSpatialReference();
      }


      fileExtension = "*.lxml";
      string[] xmlFiles = Directory.GetFiles(_filePath, fileExtension);

      var lxmlFileName = System.IO.Path.GetFileNameWithoutExtension(xmlFiles[0]);
      var lxmlFileNameExt = System.IO.Path.GetFileName(xmlFiles[0]);

      //add the feature class tables
      var ptsFeatClass = new ProPluginTableTemplate(_filePath, lxmlFileNameExt, lxmlFileName + "_POINTS", _spatialReference);
      _tables.Add(lxmlFileName + "_POINTS", ptsFeatClass);

      var lnsFeatClass = new ProPluginTableTemplate(_filePath, lxmlFileNameExt, lxmlFileName + "_LINES", _spatialReference);
      _tables.Add(lxmlFileName + "_LINES", lnsFeatClass);

      var polyFeatClass = new ProPluginTableTemplate(_filePath, lxmlFileNameExt, lxmlFileName + "_PARCELS", _spatialReference);
      _tables.Add(lxmlFileName + "_PARCELS", polyFeatClass);

      var planFeatClass = new ProPluginTableTemplate(_filePath, lxmlFileNameExt, lxmlFileName + "_PLANS", _spatialReference);
      _tables.Add(lxmlFileName + "_PLANS", planFeatClass);

    }

    /// <summary>
    /// 
    /// </summary>
    public override void Close()
    {
      //Dispose of any cached table instances here
      foreach (var table in _tables.Values)
      {
        ((ProPluginTableTemplate)table).Dispose();
      }
      _tables.Clear();
    }

    /// <summary>
    /// Open the specified table
    /// </summary>
    /// <param name="name">The name of the table to open</param>
    /// <remarks>For the sample, you can also pass in the name of the csv file<br/>
    /// e.g. "TREE_INSPECTIONS" or "tree_inspections.csv" will both work</remarks>
    /// <returns><see cref="PluginTableTemplate"/></returns>
    public override PluginTableTemplate OpenTable(string tableName)
    {
      //This is only necessary if your internals have thread affinity
      //
      //If you are using shared data (eg "static") it is your responsibility
      //to manage access to it across multiple threads.
      if (_thread_id != GetCurrentThreadId())
      {
        throw new ArcGIS.Core.CalledOnWrongThreadException();
      }

      if (!this.GetTableNames().Contains(tableName))
        throw new GeodatabaseException($"The table {tableName} was not found");

      //If you do ~not~ want to cache the csv for the lifetime of
      //your workspace instance then return a new table on each request. The edge case
      //for this sample being that the contents of the folder or individual csv's can
      //change after the data is loaded and those changes will not be reflected in a
      //given workspace instance until it is closed and re-opened.
      //
      //return new ProPluginTableTemplate(path, table_name, SpatialReferences.WGS84);

      if (!_tables.Keys.Contains(tableName))
      {
        var fileExtension = "*.lxml";
        string[] xmlFiles = Directory.GetFiles(_filePath, fileExtension);
        var file_name = xmlFiles[0];
        string path = System.IO.Path.Combine(_filePath, file_name);
        _tables[tableName] = new ProPluginTableTemplate(_filePath, file_name, tableName, _spatialReference);
      }
      return _tables[tableName];

    }

    /// <summary>
    /// Get the table names available in the workspace
    /// </summary>
    /// <returns></returns>
    public override IReadOnlyList<string> GetTableNames()
    {

      //this is accounting for more than one file in the folder,
      //however, does not apply in this case since each LandXML file defines a source
      //hence, only add names for current file
      var fileNames =
          Directory.GetFiles(_filePath, "*.lxml", System.IO.SearchOption.TopDirectoryOnly)
              .Select(fn => System.IO.Path.GetFileNameWithoutExtension(fn).ToUpper());
      var tableNames = new List<string>();
      for (int i = 0; i <  1; i++) //replaced fileNames.Count() with 1
      {
        tableNames.Add(fileNames.ToArray()[i] + "_PLANS");
        tableNames.Add(fileNames.ToArray()[i] + "_PARCELS");
        tableNames.Add(fileNames.ToArray()[i] + "_LINES");
        tableNames.Add (fileNames.ToArray()[i] + "_POINTS");
      }

      //there is an edge case where files could have been deleted after they
      //were opened...so union in the cache names
      var cachedTables = _tables.Keys;
      //return fileNames.Union(cachedTables).OrderBy(name => name).ToList();
      return tableNames;

    }

    /// <summary>
    /// Returns whether or not SQL queries are supported on the plugin
    /// </summary>
    /// <remarks>Returning false (default) means that the WhereClause of an
    /// incoming query filter will always be empty (regardless of what clients
    /// set it to)</remarks>
    /// <returns>true or false</returns>
    public override bool IsQueryLanguageSupported()
    {
      return true;
    }

  }

}
