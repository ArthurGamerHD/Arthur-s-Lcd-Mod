#if EXPERIMENTAL
using System;
using System.Collections.Generic;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    enum FilePickerMode
    {
        PickFile,
        PickFolder
    }

    sealed class FolderModel
    {
        public FolderModel()
        {
            Folders = new List<FolderModel>();
            Files = new List<FileModel>();
        }

        public string Name { get; set; }
        public string FullPath { get; set; }
        public string Subtitle { get; set; }
        public object Tag { get; set; }
        public List<FolderModel> Folders { get; private set; }
        public List<FileModel> Files { get; private set; }
        public FolderModel Parent { get; internal set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(FullPath) ? (Name ?? string.Empty) : FullPath;
        }
    }

    sealed class FileModel
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public string IconPath { get; set; }
        public string Subtitle { get; set; }
        public object Tag { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(FullPath) ? (Name ?? string.Empty) : FullPath;
        }
    }

    sealed class FilePickerResult
    {
        public FilePickerMode Mode { get; set; }
        public string RootName { get; set; }
        public string FullPath { get; set; }
        public FolderModel Folder { get; set; }
        public FileModel File { get; set; }
        public object Tag { get; set; }
    }

    sealed class FilePickerContextAction
    {
        public FilePickerContextAction()
        {
        }

        public FilePickerContextAction(string text, Action clicked)
        {
            Text = text;
            Clicked = clicked;
        }

        public string Text { get; set; }
        public Action Clicked { get; set; }
        public bool Enabled { get; set; } = true;
    }

    abstract class FilePickerEntryModel
    {
        public bool IsSelected;
        public bool IsUpEntry;
        public string Name;
        public string FullPath;
        public string Subtitle;
        public string Icon;
    }

    sealed class FolderControlModel : FilePickerEntryModel
    {
        public FolderModel Folder;
    }

    sealed class FileControlModel : FilePickerEntryModel
    {
        public FileModel File;
    }
}
#endif
