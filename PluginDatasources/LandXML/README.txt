The add-in is not production ready, and should be considered an alpha/prototype and a proof of concept.
The add-in code does not currently handle "local" coordinates such as those close to the 0,0 origin of the coordinate system.
The land xml files need an extension of "lxml" to be recognized as a Plugin Datasource in Pro.
The code, as currently written, uses/requires the spatial reference file (.prj file) in the same folder location on disk as the .lxml file
The code, as currently written, requires the .lxml file to be in a folder on its own without any other .lxml files.

For demonstration of its use, see following meetup video recording starting 1:05:15 - 
https://community.esri.com/t5/arcgis-parcel-fabric-videos/meetup-arcgis-parcel-fabric-what-s-new-at-3-6/m-p/1666922#M264