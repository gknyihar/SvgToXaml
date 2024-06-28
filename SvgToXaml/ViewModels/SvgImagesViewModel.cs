﻿using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using SvgConverter;
using SvgToXaml.Command;
using SvgToXaml.Infrastructure;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SvgToXaml.ViewModels
{
    public class SvgImagesViewModel : ViewModelBase
    {
        private string _currentDir;
        private ObservableCollectionSafe<ImageBaseViewModel> _images;
        private ImageBaseViewModel _selectedItem;

        public SvgImagesViewModel()
        {
            _images = new ObservableCollectionSafe<ImageBaseViewModel>();
            OpenFileCommand = new DelegateCommand(OpenFileExecute);
            OpenFolderCommand = new DelegateCommand(OpenFolderExecute);
            ExportDirCommand = new DelegateCommand(ExportDirExecute);
            ExportResDictCommand = new DelegateCommand(ExportResDictExecute);
            InfoCommand = new DelegateCommand(InfoExecute);

            ContextMenuCommands = new ObservableCollection<Tuple<object, ICommand>>();
            ContextMenuCommands.Add(new Tuple<object, ICommand>("Open Explorer", new DelegateCommand<string>(OpenExplorerExecute)));
        }

        private void OpenFolderExecute()
        {
            var folderDialog = new FolderBrowserDialog { Description = "Open Folder", SelectedPath = CurrentDir, ShowNewFolderButton = false };
            if (folderDialog.ShowDialog() == DialogResult.OK)
                CurrentDir = folderDialog.SelectedPath;
        }

        private void OpenFileExecute()
        {
            var openDlg = new OpenFileDialog { CheckFileExists = true, Filter = "Svg-Files|*.svg*", Multiselect = false };
            if (openDlg.ShowDialog().GetValueOrDefault())
            {
                ImageBaseViewModel.OpenDetailWindow(new SvgImageViewModel(openDlg.FileName));
            }
        }

        private void ExportDirExecute()
        {
            string outFileName = Path.GetFileNameWithoutExtension(CurrentDir) + ".xaml";
            var saveDlg = new SaveFileDialog { AddExtension = true, DefaultExt = ".xaml", Filter = "Xaml-File|*.xaml", InitialDirectory = CurrentDir, FileName = outFileName };
            if (saveDlg.ShowDialog() == DialogResult.OK)
            {
                string namePrefix = null;

                bool useComponentResKeys = false;
                string nameSpaceName = null;
                var nameSpace = Microsoft.VisualBasic.Interaction.InputBox("Enter a NameSpace for using static ComponentResKeys (or leave empty to not use it)", "NameSpace");
                if (!string.IsNullOrWhiteSpace(nameSpace))
                {
                    useComponentResKeys = true;
                    nameSpaceName =
                        Microsoft.VisualBasic.Interaction.InputBox(
                            "Enter a Name of NameSpace for using static ComponentResKeys", "NamespaceName");
                }
                else
                {
                    namePrefix = Microsoft.VisualBasic.Interaction.InputBox("Enter a namePrefix (or leave empty to not use it)", "Name Prefix");
                    if (string.IsNullOrWhiteSpace(namePrefix))
                        namePrefix = null;

                }

                outFileName = Path.GetFullPath(saveDlg.FileName);
                var resKeyInfo = new ResKeyInfo
                {
                    XamlName = Path.GetFileNameWithoutExtension(outFileName),
                    Prefix = namePrefix,
                    UseComponentResKeys = useComponentResKeys,
                    NameSpace = nameSpace,
                    NameSpaceName = nameSpaceName,

                };
                File.WriteAllText(outFileName, ConverterLogic.SvgDirToXaml(CurrentDir, resKeyInfo, false));

                BuildBatchFile(outFileName, resKeyInfo);
            }
        }

        private void ExportResDictExecute()
        {
            string outFileName = Path.GetFileNameWithoutExtension(CurrentDir) + ".xaml";
            var saveDlg = new SaveFileDialog { AddExtension = true, DefaultExt = ".xaml", Filter = "Xaml-File|*.xaml", InitialDirectory = CurrentDir, FileName = outFileName };
            if (saveDlg.ShowDialog() == DialogResult.OK)
            {
                bool useComponentResKeys = false;

                outFileName = Path.GetFullPath(saveDlg.FileName);
                var resKeyInfo = new ResKeyInfo
                {
                    XamlName = Path.GetFileNameWithoutExtension(outFileName),
                    Prefix = null,
                    UseComponentResKeys = useComponentResKeys,
                    NameSpace = null,
                    NameSpaceName = null,

                };

                var postfix = Path.GetFileNameWithoutExtension(outFileName);
                var postfixFirstChar = char.ToUpperInvariant(postfix[0]);
                postfix = postfixFirstChar + postfix.Substring(1, postfix.Last() == 's' ? postfix.Length - 2 : postfix.Length - 1);

                postfix = Microsoft.VisualBasic.Interaction.InputBox("Enter a postfix (or leave empty to not use it)", "Postfix", postfix);
                if (string.IsNullOrWhiteSpace(postfix))
                    postfix = null;

                var folder = Path.GetFileNameWithoutExtension(outFileName);
                var absoluteFolder = Path.Combine(Path.GetDirectoryName(outFileName), folder);
                if (!Directory.Exists(absoluteFolder))
                {
                    Directory.CreateDirectory(absoluteFolder);
                }
                var dict = ConverterLogic.SvgDirToXamlDicts(CurrentDir, resKeyInfo, null, false, false, postfix);
                foreach (var d in dict)
                {
                    var dictFileName = Path.Combine(absoluteFolder, $"{d.Key}.xaml");
                    File.WriteAllText(dictFileName, d.Value);
                }
                var wrapper = ConverterLogic.WrapperFileFromList(dict, folder);
                File.WriteAllText(outFileName, wrapper);

                BuildBatchFile(outFileName, resKeyInfo, true, postfix);
            }
        }

        private void BuildBatchFile(string outFileName, ResKeyInfo compResKeyInfo, bool isDict = false, string postfix = null)
        {
            if (MessageBox.Show(outFileName + " has been written\nCreate a BatchFile to automate next time?",
                "Batch", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                var outputname = Path.GetFileNameWithoutExtension(outFileName);
                var outputdir = Path.GetDirectoryName(outFileName);
                var relOutputDir = FileUtils.MakeRelativePath(CurrentDir, PathIs.Folder, outputdir, PathIs.Folder);
                var svgToXamlPath = System.Reflection.Assembly.GetEntryAssembly().Location;
                var relSvgToXamlPath = FileUtils.MakeRelativePath(CurrentDir, PathIs.Folder, svgToXamlPath, PathIs.File);
                var batchText = $"{relSvgToXamlPath} BuildDict /inputdir=\".\" /outputdir=\"{relOutputDir}\" /outputname=\"{outputname}\"";
                if (isDict)
                    batchText += postfix == null ? " /createResourceDictionary=true" : $" /createResourceDictionary=true /postfix=\"{postfix}\"";

                if (compResKeyInfo.UseComponentResKeys)
                {
                    batchText += $" /useComponentResKeys=true /compResKeyNSName={compResKeyInfo.NameSpaceName} /compResKeyNS={compResKeyInfo.NameSpace}";
                    WriteT4Template(outFileName);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(compResKeyInfo.Prefix))
                    {
                        batchText += " /nameprefix \"" + compResKeyInfo.Prefix + "\"";
                    }
                }

                batchText += "\r\npause";

                File.WriteAllText(Path.Combine(CurrentDir, "Update.cmd"), batchText);

                ////Copy ExeFile
                //var srcFile = Environment.GetCommandLineArgs().First();
                //var destFile = Path.Combine(CurrentDir, Path.GetFileName(srcFile));
                ////Console.WriteLine("srcFile:", srcFile);
                ////Console.WriteLine("destFile:", destFile);
                //if (!string.Equals(srcFile, destFile, StringComparison.OrdinalIgnoreCase))
                //{
                //    Console.WriteLine("Copying file...");
                //    File.Copy(srcFile, destFile, true);
                //}
            }
        }

        private void WriteT4Template(string outFileName)
        {
            //BuildAction: "Embedded Resource"
            var appType = typeof(App);
            var assembly = appType.Assembly;
            //assembly.GetName().Name
            var resourceName = appType.Namespace + "." + "Payload.T4Template.tt"; //Achtung: hier Punkt statt Slash
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new InvalidDataException($"Error: {resourceName} not found in payload file");
            var text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            var t4FileName = Path.ChangeExtension(outFileName, ".tt");
            File.WriteAllText(t4FileName, text, Encoding.UTF8);
        }

        private void InfoExecute()
        {
            MessageBox.Show("SvgToXaml © 2015 Bernd Klaiber\n\nPowered by\nsharpvectors.codeplex.com (Svg-Support),\nicsharpcode (AvalonEdit)", "Info");
        }
        private void OpenExplorerExecute(string path)
        {
            Process.Start(path);
        }

        public static SvgImagesViewModel DesignInstance
        {
            get
            {
                var result = new SvgImagesViewModel();
                result.Images.Add(SvgImageViewModel.DesignInstance);
                result.Images.Add(SvgImageViewModel.DesignInstance);
                return result;
            }
        }

        public string CurrentDir
        {
            get { return _currentDir; }
            set
            {
                if (SetProperty(ref _currentDir, value))
                    ReadImagesFromDir(_currentDir);
            }
        }

        public ImageBaseViewModel SelectedItem
        {
            get { return _selectedItem; }
            set { SetProperty(ref _selectedItem, value); }
        }

        public ObservableCollectionSafe<ImageBaseViewModel> Images
        {
            get { return _images; }
            set { SetProperty(ref _images, value); }
        }

        public ICommand OpenFolderCommand { get; set; }
        public ICommand OpenFileCommand { get; set; }
        public ICommand ExportDirCommand { get; set; }
        public ICommand ExportResDictCommand { get; set; }
        public ICommand InfoCommand { get; set; }

        public ObservableCollection<Tuple<object, ICommand>> ContextMenuCommands { get; set; }

        private void ReadImagesFromDir(string folder)
        {
            Images.Clear();
            var svgFiles = ConverterLogic.SvgFilesFromFolder(folder);
            var svgImages = svgFiles.Select(f => new SvgImageViewModel(f));

            var graphicFiles = GetFilesMulti(folder, GraphicImageViewModel.SupportedFormats);
            var graphicImages = graphicFiles.Select(f => new GraphicImageViewModel(f));

            var allImages = svgImages.Concat<ImageBaseViewModel>(graphicImages).OrderBy(e => e.Filepath);

            Images.AddRange(allImages);
        }

        private static string[] GetFilesMulti(string sourceFolder, string filters, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            try
            {
                if (!Directory.Exists(sourceFolder))
                    return new string[0];
                return filters.Split('|').SelectMany(filter => Directory.GetFiles(sourceFolder, filter, searchOption)).ToArray();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }
    }
}
