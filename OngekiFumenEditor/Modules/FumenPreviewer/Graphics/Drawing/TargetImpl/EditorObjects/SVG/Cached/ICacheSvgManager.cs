﻿using OngekiFumenEditor.Base.EditorObjects.Svg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.ILineDrawing;

namespace OngekiFumenEditor.Modules.FumenPreviewer.Graphics.Drawing.TargetImpl.EditorObjects.SVG.Cached
{
    public interface ICachedSvgRenderDataManager
    {
        public List<LineVertex> GetRenderData(IFumenEditorDrawingContext target, SvgPrefabBase svgPrefab, out bool isCached, out Rect bound);
    }
}
