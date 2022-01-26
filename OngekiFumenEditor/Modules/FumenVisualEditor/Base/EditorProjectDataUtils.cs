﻿using Caliburn.Micro;
using OngekiFumenEditor.Modules.FumenVisualEditor.Models;
using OngekiFumenEditor.Parser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OngekiFumenEditor.Modules.FumenVisualEditor.Base
{
    public class EditorProjectDataUtils
    {
        private static JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true
        };

        public static string GetRelativeOngekiFumenFilePath(string editorProjectFilePath) => Path.Combine(Path.GetDirectoryName(editorProjectFilePath), Path.GetFileNameWithoutExtension(editorProjectFilePath) + ".ogkr");

        public static async Task<EditorProjectDataModel> TryLoadFromFileAsync(string filePath)
        {
            using var fileStream = File.OpenRead(filePath);
            var projectData = await JsonSerializer.DeserializeAsync<EditorProjectDataModel>(fileStream);

            var fumenFilePath = projectData.FumenFilePath ?? GetRelativeOngekiFumenFilePath(filePath);
            if (projectData.FumenFilePath is null)
                projectData.FumenFilePath = fumenFilePath;
            using var fumenFileStream = File.OpenRead(fumenFilePath);
            var fumen = await IoC.Get<IOngekiFumenParser>().ParseAsync(fumenFileStream);
            projectData.Fumen = fumen;

            ApplyBulletPalleteListEditorData(projectData);

            return projectData;
        }

        private static void ApplyBulletPalleteListEditorData(EditorProjectDataModel projectData)
        {
            foreach (var bpl in projectData.Fumen.BulletPalleteList)
            {
                if (projectData.StoreBulletPalleteEditorDatas.TryGetValue(bpl.StrID, out var storeEditorData))
                {
                    bpl.EditorName = storeEditorData.Name;
                    bpl.EditorAxuiliaryLineColor = storeEditorData.AuxiliaryLineColor;
                }
            }
        }

        private static void StoreBulletPalleteListEditorData(EditorProjectDataModel projectData)
        {
            foreach (var bpl in projectData.Fumen.BulletPalleteList)
            {
                if (projectData.StoreBulletPalleteEditorDatas.TryGetValue(bpl.StrID, out var storeEditorData))
                {
                    storeEditorData.Name = bpl.EditorName;
                    storeEditorData.AuxiliaryLineColor = bpl.EditorAxuiliaryLineColor;
                }
                else
                {
                    projectData.StoreBulletPalleteEditorDatas[bpl.StrID] = new()
                    {
                        AuxiliaryLineColor = bpl.EditorAxuiliaryLineColor,
                        Name = bpl.EditorName
                    };
                }
            }
        }

        public static async Task TrySaveToFileAsync(string filePath, EditorProjectDataModel editorProject)
        {
            using var fileStream = File.OpenWrite(filePath);
            StoreBulletPalleteListEditorData(editorProject);

            var fumenFilePath = editorProject.FumenFilePath ?? GetRelativeOngekiFumenFilePath(filePath);
            if (editorProject.FumenFilePath is null)
                editorProject.FumenFilePath = fumenFilePath;

            await JsonSerializer.SerializeAsync(fileStream, editorProject, JsonSerializerOptions);
            await File.WriteAllTextAsync(fumenFilePath, editorProject.Fumen.Serialize());
        }
    }
}
