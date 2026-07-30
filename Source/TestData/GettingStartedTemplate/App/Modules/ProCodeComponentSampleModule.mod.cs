void OnAfterInitialize()
{
    SelectableList.DeleteAllRows();
    foreach (var name in new string[] { "サンプル1", "サンプル2", "サンプル3", "サンプル4", "サンプル5" })
    {
        var row = SelectableList.AddRow();
        row.Name.Value = name;
    }
}
