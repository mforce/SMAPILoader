using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using SMAPIGameLoader.Tool;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAPIGameLoader.Launcher;

internal static class SaveManager
{
    const string SavesDirName = "Saves";
    public static string GetSavesDir() => Path.Combine(FileTool.ExternalFilesDir, SavesDirName);
    public static void OnClickImportSaveZip(object sender, EventArgs args)
    {
        DialogTool.Show("Save Importer",
            "Importing overwrites farms with the same name. Export your current saves first if you want to keep them.",
            buttonOKName: "Import",
            onClickYes: ImportSaveZip,
            onClickCancel: () => { });
    }

    static async void ImportSaveZip()
    {
        try
        {
            await ImportSaveZipInternal();
        }
        catch (Exception ex)
        {
            ErrorDialogTool.Show(ex);
        }
    }

    const string SaveGameInfo_FileName = "SaveGameInfo";

    static async Task ImportSaveZipInternal()
    {
        var pick = await FilePickerTool.PickZipFile();
        if (pick is null)
            return;

        if (!pick.FileName.StartsWith(SavesDirName) || !pick.FileName.EndsWith(".zip"))
        {
            DialogTool.Show("Save Importer Error", "Please pick a Saves*.zip file, for example Saves.zip or an exported backup.");
            return;
        }

        //extract it
        HashSet<string> savesImportHashset = new();

        using (var zip = ZipFile.OpenRead(pick.FullPath))
        {
            var savesDirPath = GetSavesDir();
            const string SkipEntrySavesName = SavesDirName + "/";
            var entires = zip.Entries.ToArray();
            foreach (var entry in entires)
            {
                //skip entry name folder 'Saves/'
                if (entry.FullName == SkipEntrySavesName)
                    continue;

                if (FileTool.IsEntryDirectory(entry))
                    continue;

                var firstDirName = FileTool.GetFirstDirNameFromFilePath(entry.FullName);
                var currentEntryFilePath = entry.FullName;
                if (firstDirName != null && firstDirName == SavesDirName)
                    currentEntryFilePath = currentEntryFilePath.Remove(0, SkipEntrySavesName.Length);

                var extractFilePath = Path.Combine(savesDirPath, currentEntryFilePath);
                ZipFileTool.Extract(entry, extractFilePath);
                var fileInfo = new FileInfo(extractFilePath);
                if (fileInfo.Name == SaveGameInfo_FileName)
                {
                    var parentDir = fileInfo.Directory;
                    savesImportHashset.Add(parentDir.Name);
                }
            }

            //options safety first
            RemoveEmergencySaveFile();
        }

        StringBuilder sbSaveImported = new();
        sbSaveImported.AppendLine($"Successfully import saves {savesImportHashset.Count}");

        {
            int saveIndexCounter = 0;
            foreach (var saveDirName in savesImportHashset)
            {
                saveIndexCounter++;
                sbSaveImported.AppendLine($"[{saveIndexCounter}] {saveDirName}");
            }
        }

        DialogTool.Show("Save Importer", sbSaveImported.ToString());
    }
    public static void OnClickExportSaves(object sender, EventArgs args)
    {
        try
        {
            ExportSavesInternal();
        }
        catch (Exception ex)
        {
            ErrorDialogTool.Show(ex);
        }
    }

    const string ExportRelativeDir = "Download/SMAPI";

    // ponytail: Android 10+ only (MediaStore.Downloads); Android 9 needs a WRITE_EXTERNAL_STORAGE path if anyone asks.
    static void ExportSavesInternal()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q)
        {
            DialogTool.Show("Save Exporter", "Export needs Android 10 or newer.");
            return;
        }

        var savesDir = GetSavesDir();
        if (!Directory.Exists(savesDir) || !Directory.EnumerateFileSystemEntries(savesDir).Any())
        {
            DialogTool.Show("Save Exporter", "No saves found.");
            return;
        }

        var fileName = $"{SavesDirName}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.zip";
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(MediaStore.IMediaColumns.MimeType, "application/zip");
        values.Put(MediaStore.IMediaColumns.RelativePath, ExportRelativeDir);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var resolver = Application.Context.ContentResolver;
        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values)
            ?? throw new IOException("Could not create the backup file in Downloads.");
        try
        {
            using (var stream = resolver.OpenOutputStream(uri))
                ZipFile.CreateFromDirectory(savesDir, stream, CompressionLevel.Optimal, includeBaseDirectory: true);
        }
        catch
        {
            // a half-written zip must not look like a valid backup
            resolver.Delete(uri, null, null);
            throw;
        }

        values.Clear();
        values.Put(MediaStore.IMediaColumns.IsPending, 0);
        resolver.Update(uri, values, null, null);

        DialogTool.Show("Save Exporter", $"Saves exported to {ExportRelativeDir}/{fileName}");
    }

    const string EMERGENCY_SAVE_FileName = "EMERGENCY_SAVE";
    public static void RemoveEmergencySaveFile()
    {
        string filePath = Path.Combine(GetSavesDir(), EMERGENCY_SAVE_FileName);
        FileTool.Delete(filePath);
    }
}
