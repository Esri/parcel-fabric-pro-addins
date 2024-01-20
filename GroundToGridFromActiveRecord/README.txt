To install this add-in double-click the file with the esriAddinX extension, and then start or restart Pro.

Description:
This add-in sets the ground to grid values when changing the active record.

To use the add-in:
To opt-in create two [double] fields on the Records feature class, and call the fields 'DistanceFactor' and 'DirectionOffset'.
Type in, or calculate the appropriate direction offset and combined scale factor values into these fields for the records in the records feature class.
The map's ground to grid correction will change to the (non-null) values found in these fields whenever a record is activated. If the value in the activated record is null, then it will not change the currently set ground to grid value.
Use the add-in button called 'Save Ground To Grid To Record' on the active record's heads up display menu to save the map's ground to grid correction values to these fields. Existing values will be overwritten.
