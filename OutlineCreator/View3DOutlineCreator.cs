/*
Copyright (c) 2014, Lars Brubaker
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.IO;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.OpenGlGui;
using MatterHackers.Agg.Font;
using MatterHackers.PolygonMesh;
using MatterHackers.RenderOpenGl;
using MatterHackers.VectorMath;
using MatterHackers.MeshVisualizer;
using MatterHackers.PolygonMesh.Processors;
using MatterHackers.PolygonMesh.Csg;
using MatterHackers.MarchingSquares;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.Agg.ImageProcessing;
using MatterHackers.Agg.VertexSource;
using MatterHackers.MatterControl;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.RayTracer;
using MatterHackers.RayTracer.Traceable;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.PrintLibrary;

using ClipperLib;

using OpenTK.Graphics.OpenGL;

namespace MatterHackers.MatterControl.Plugins.OutlineCreator
{
    using Polygon = List<IntPoint>;
    using Polygons = List<List<IntPoint>>;

    public class View3DOutlineCreator : PartPreview3DWidget
    {
        Slider sizeScrollBar;
        Slider heightScrollBar;
        Slider rotationScrollBar;
        
        double lastHeightValue = 1;
        double lastSizeValue = 1;

        ProgressControl processingProgressControl;
        FlowLayoutWidget editPlateButtonsContainer;

        Button saveButton;
        Button saveAndExitButton;
        Button closeButton;
        PrintItem printItem;
        PrintItemWrapper printItemWrapper;
        PrintLibraryListItem queueItem;


        List<Mesh> asynchMeshesList = new List<Mesh>();
        List<Matrix4X4> asynchMeshTransforms = new List<Matrix4X4>();
        List<PlatingMeshData> asynchPlatingDataList = new List<PlatingMeshData>();

        List<PlatingMeshData> MeshExtraData;

        public Matrix4X4 SelectedMeshTransform
        {
            get { return meshViewerWidget.SelectedMeshTransform; }
            set { meshViewerWidget.SelectedMeshTransform = value; }
        }

        public Mesh SelectedMesh
        {
            get { return meshViewerWidget.SelectedMesh; }
        }

        public int SelectedMeshIndex
        {
            get { return meshViewerWidget.SelectedMeshIndex; }
            set { meshViewerWidget.SelectedMeshIndex = value; }
        }

        public List<Mesh> Meshes
        {
            get { return meshViewerWidget.Meshes; }
        }

        public List<Matrix4X4> MeshTransforms
        {
            get { return meshViewerWidget.MeshTransforms; }
        }

        internal struct MeshSelectInfo
        {
            internal bool downOnPart;
            internal PlaneShape hitPlane;
            internal Vector3 planeDownHitPos;
            internal Vector3 lastMoveDelta;
        }

        TypeFace boldTypeFace;
        public View3DOutlineCreator(Vector3 viewerVolume, MeshViewerWidget.BedShape bedShape)
        {
            string staticDataPath = DataStorage.ApplicationDataStorage.Instance.ApplicationStaticDataPath;
            string fontPath = Path.Combine(staticDataPath, "Fonts", "LiberationSans-Bold.svg");
            boldTypeFace = TypeFace.LoadSVG(fontPath);

            MeshExtraData = new List<PlatingMeshData>();

            FlowLayoutWidget mainContainerTopToBottom = new FlowLayoutWidget(FlowDirection.TopToBottom);
            mainContainerTopToBottom.HAnchor = Agg.UI.HAnchor.Max_FitToChildren_ParentWidth;
            mainContainerTopToBottom.VAnchor = Agg.UI.VAnchor.Max_FitToChildren_ParentHeight;

            FlowLayoutWidget centerPartPreviewAndControls = new FlowLayoutWidget(FlowDirection.LeftToRight);
            centerPartPreviewAndControls.AnchorAll();

            GuiWidget viewArea = new GuiWidget();
            viewArea.AnchorAll();
            {
                meshViewerWidget = new MeshViewerWidget(viewerVolume, 1, bedShape);
                meshViewerWidget.AlwaysRenderBed = true;
                meshViewerWidget.AnchorAll();
            }
            viewArea.AddChild(meshViewerWidget);

            centerPartPreviewAndControls.AddChild(viewArea);
            mainContainerTopToBottom.AddChild(centerPartPreviewAndControls);

            FlowLayoutWidget buttonBottomPanel = new FlowLayoutWidget(FlowDirection.LeftToRight);
            buttonBottomPanel.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.Padding = new BorderDouble(3, 3);
            buttonBottomPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;

            buttonRightPanel = CreateRightButtonPannel(viewerVolume.y);

            // add in the plater tools
            {
                FlowLayoutWidget editToolBar = new FlowLayoutWidget();

                processingProgressControl = new ProgressControl("Finding Parts:");
                processingProgressControl.VAnchor = Agg.UI.VAnchor.ParentCenter;
                editToolBar.AddChild(processingProgressControl);
                editToolBar.VAnchor |= Agg.UI.VAnchor.ParentCenter;

                editPlateButtonsContainer = new FlowLayoutWidget();

                Button addButton = textImageButtonFactory.Generate("Add Outlines", "icon_circle_plus.png");
                addButton.Margin = new BorderDouble(right: 10);
                editPlateButtonsContainer.AddChild(addButton);
                addButton.Click += (sender, e) =>
                {
                    UiThread.RunOnIdle((state) =>
                    {
                        OpenFileDialogParams openParams = new OpenFileDialogParams("Select an image file|*.jpg;*.png;*.bmp", multiSelect: true, title: "Add Outlines");

                        // we do this using to make sure that the stream is closed before we try and insert the outlines
                        using (Stream stream = FileDialog.OpenFileDialog(ref openParams))
                        {
                        }
                        InsertOutlinesNow(openParams);
                    });
                };

                KeyDown += (sender, e) =>
                {
                    KeyEventArgs keyEvent = e as KeyEventArgs;
                    if (keyEvent != null && !keyEvent.Handled)
                    {
                        if (keyEvent.KeyCode == Keys.Delete || keyEvent.KeyCode == Keys.Back)
                        {
                            DeleteSelectedMesh();
                        }

                        if (keyEvent.KeyCode == Keys.Escape)
                        {
                            if (meshSelectInfo.downOnPart)
                            {
                                meshSelectInfo.downOnPart = false;
                                SelectedMeshTransform = transformOnMouseDown;
                                Invalidate();
                            }
                        }
                    }
                };

                editToolBar.AddChild(editPlateButtonsContainer);
                buttonBottomPanel.AddChild(editToolBar);
            }

            GuiWidget buttonRightPanelHolder = new GuiWidget(HAnchor.FitToChildren, VAnchor.ParentBottomTop);
            centerPartPreviewAndControls.AddChild(buttonRightPanelHolder);
            buttonRightPanelHolder.AddChild(buttonRightPanel);

            buttonRightPanelDisabledCover = new Cover(HAnchor.ParentLeftRight, VAnchor.ParentBottomTop);
            buttonRightPanelDisabledCover.BackgroundColor = new RGBA_Bytes(ActiveTheme.Instance.PrimaryBackgroundColor, 150);
            buttonRightPanelHolder.AddChild(buttonRightPanelDisabledCover);
            LockEditControls();

            GuiWidget leftRightSpacer = new GuiWidget();
            leftRightSpacer.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.AddChild(leftRightSpacer);

            closeButton = textImageButtonFactory.Generate("Close");
            buttonBottomPanel.AddChild(closeButton);

            mainContainerTopToBottom.AddChild(buttonBottomPanel);

            this.AddChild(mainContainerTopToBottom);
            this.AnchorAll();

            meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.Rotation;

            Add3DViewControls();
            rotateViewButton.Click += (sender, e) =>
            {
                meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.Rotation;
            };
            translateButton.Click += (sender, e) =>
            {
                meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.Translation;
            };
            scaleButton.Click += (sender, e) =>
            {
                meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.Scale;
            };
            partSelectButton.Click += (sender, e) =>
            {
                meshViewerWidget.TrackballTumbleWidget.TransformState = TrackBallController.MouseDownType.None;
            };
            partSelectButton.CheckedStateChanged += (sender, e) =>
            {
                SetMeshViewerDisplayTheme();
            };

            // set the view to be a good angle and distance
            meshViewerWidget.TrackballTumbleWidget.TrackBallController.Scale = .06;
            meshViewerWidget.TrackballTumbleWidget.TrackBallController.Rotate(Quaternion.FromEulerAngles(new Vector3(-MathHelper.Tau * .02, 0, 0)));

            AddHandlers();
            UnlockEditControls();
            // but make sure we can't use the right pannel yet
            buttonRightPanelDisabledCover.Visible = true;

            SetMeshViewerDisplayTheme();
        }

        private void InsertOutlinesNow(OpenFileDialogParams openParams)
        {
            if (openParams.FileNames.Length > 0)
            {
                ResetWordLayoutSettings();
                processingProgressControl.textWidget.Text = "Inserting Outlines";
                processingProgressControl.Visible = true;
                processingProgressControl.PercentComplete = 0;
                LockEditControls();

                BackgroundWorker insertTextBackgroundWorker = null;
                insertTextBackgroundWorker = new BackgroundWorker();
                insertTextBackgroundWorker.WorkerReportsProgress = true;

                insertTextBackgroundWorker.DoWork += new DoWorkEventHandler(insertOutlinesBackgroundWorker_DoWork);
                insertTextBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(BackgroundWorker_ProgressChanged);
                insertTextBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(insertOutlinesBackgroundWorker_RunWorkerCompleted);

                insertTextBackgroundWorker.RunWorkerAsync(openParams);
            }
        }

        private void ResetWordLayoutSettings()
        {
            rotationScrollBar.Value = 1;
            sizeScrollBar.Value = 1;
            heightScrollBar.Value = .25;
            lastHeightValue = 1;
            lastSizeValue = 1;
        }

        private bool FindMeshHitPosition(Vector2 screenPosition, out int meshHitIndex)
        {
            meshHitIndex = 0;
            if (MeshExtraData.Count == 0 || MeshExtraData[0].traceableData == null)
            {
                return false;
            }

            List<IRayTraceable> mesheTraceables = new List<IRayTraceable>();
            for (int i = 0; i < MeshExtraData.Count; i++)
            {
                mesheTraceables.Add(new Transform(MeshExtraData[i].traceableData, MeshTransforms[i]));
            }
            IRayTraceable allObjects = BoundingVolumeHierarchy.CreateNewHierachy(mesheTraceables);

            Ray ray = meshViewerWidget.TrackballTumbleWidget.LastScreenRay;
            IntersectInfo info = allObjects.GetClosestIntersection(ray);
            if (info != null)
            {
                meshSelectInfo.planeDownHitPos = info.hitPosition;
                meshSelectInfo.lastMoveDelta = new Vector3();

                for (int i = 0; i < MeshExtraData.Count; i++)
                {
                    List<IRayTraceable> insideBounds = new List<IRayTraceable>();
                    MeshExtraData[i].traceableData.GetContained(insideBounds, info.closestHitObject.GetAxisAlignedBoundingBox());
                    if (insideBounds.Contains(info.closestHitObject))
                    {
                        meshHitIndex = i;
                        return true;
                    }
                }
            }

            return false;
        }

        Matrix4X4 transformOnMouseDown = Matrix4X4.Identity;
        MeshSelectInfo meshSelectInfo;
        public override void OnMouseDown(MouseEventArgs mouseEvent)
        {
            base.OnMouseDown(mouseEvent);
            if (meshViewerWidget.TrackballTumbleWidget.UnderMouseState == Agg.UI.UnderMouseState.FirstUnderMouse)
            {
                if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None)
                {
                    partSelectButton.ClickButton(null);
                    int meshHitIndex;
                    if (FindMeshHitPosition(mouseEvent.Position, out meshHitIndex))
                    {
                        meshSelectInfo.hitPlane = new PlaneShape(Vector3.UnitZ, meshSelectInfo.planeDownHitPos.z, null);
                        SelectedMeshIndex = meshHitIndex;
                        transformOnMouseDown = SelectedMeshTransform;
                        Invalidate();
                        meshSelectInfo.downOnPart = true;
                    }
                }
            }
        }

        public override void OnDraw(Graphics2D graphics2D)
        {
            //DoCsgTest();
            base.OnDraw(graphics2D);
        }

        public override void OnMouseMove(MouseEventArgs mouseEvent)
        {
            if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None && meshSelectInfo.downOnPart)
            {
                Ray ray = meshViewerWidget.TrackballTumbleWidget.LastScreenRay;
                IntersectInfo info = meshSelectInfo.hitPlane.GetClosestIntersection(ray);
                if (info != null)
                {
                    Vector3 delta = info.hitPosition - meshSelectInfo.planeDownHitPos;

                    Matrix4X4 totalTransfrom = Matrix4X4.CreateTranslation(new Vector3(-meshSelectInfo.lastMoveDelta));
                    totalTransfrom *= Matrix4X4.CreateTranslation(new Vector3(delta));
                    meshSelectInfo.lastMoveDelta = delta;
                    SelectedMeshTransform *= totalTransfrom;
                    Invalidate();
                }
            }

            base.OnMouseMove(mouseEvent);
        }

        public override void OnMouseUp(MouseEventArgs mouseEvent)
        {
            if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None
                && meshSelectInfo.downOnPart
                && meshSelectInfo.lastMoveDelta != Vector3.Zero)
            {
                saveButton.Visible = true;
                saveAndExitButton.Visible = true;
            }

            meshSelectInfo.downOnPart = false;

            base.OnMouseUp(mouseEvent);
        }

        private void MakeCopyOfMesh()
        {
            if (Meshes.Count > 0)
            {
                processingProgressControl.textWidget.Text = "Making Copy:";
                processingProgressControl.Visible = true;
                processingProgressControl.PercentComplete = 0;
                LockEditControls();

                BackgroundWorker copyPartBackgroundWorker = null;
                copyPartBackgroundWorker = new BackgroundWorker();
                copyPartBackgroundWorker.WorkerReportsProgress = true;

                copyPartBackgroundWorker.DoWork += new DoWorkEventHandler(copyPartBackgroundWorker_DoWork);
                copyPartBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(BackgroundWorker_ProgressChanged);
                copyPartBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(copyPartBackgroundWorker_RunWorkerCompleted);

                copyPartBackgroundWorker.RunWorkerAsync();
            }
        }

        void copyPartBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            UnlockEditControls();
            PullMeshDataFromAsynchLists();
            saveButton.Visible = true;
            saveAndExitButton.Visible = true;
            partSelectButton.ClickButton(null);

            // now set the selection to the new copy
            MeshExtraData[Meshes.Count - 1].currentScale = MeshExtraData[SelectedMeshIndex].currentScale;
            SelectedMeshIndex = Meshes.Count - 1;
        }

        void copyPartBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            BackgroundWorker backgroundWorker = (BackgroundWorker)sender;

            PushMeshDataToAsynchLists(true);

            Mesh copyMesh = new Mesh();

            int faceCount = asynchMeshesList[SelectedMeshIndex].Faces.Count;
            for (int i = 0; i < faceCount; i++)
            {
                Face face = asynchMeshesList[SelectedMeshIndex].Faces[i];
                List<Vertex> faceVertices = new List<Vertex>();
                foreach (FaceEdge faceEdgeToAdd in face.FaceEdges())
                {
                    Vertex newVertex = copyMesh.CreateVertex(faceEdgeToAdd.firstVertex.Position, true);
                    faceVertices.Add(newVertex);
                }

                int nextPercent = (i + 1) * 80 / faceCount;
                backgroundWorker.ReportProgress(nextPercent);

                copyMesh.CreateFace(faceVertices.ToArray(), true);
            }

            PlatingHelper.FindPositionForPartAndAddToPlate(copyMesh, SelectedMeshTransform, asynchPlatingDataList, asynchMeshesList, asynchMeshTransforms);
            PlatingHelper.CreateITraceableForMesh(asynchPlatingDataList, asynchMeshesList, asynchMeshesList.Count - 1);

            backgroundWorker.ReportProgress(95);
        }

        void insertOutlinesBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            BackgroundWorker backgroundWorker = (BackgroundWorker)sender;

            PushMeshDataToAsynchLists(true);

            OpenFileDialogParams openParams = (OpenFileDialogParams)e.Argument;

            foreach (string imagePathAndFile in openParams.FileNames)
            {
                ImageBuffer imageToOutline = new ImageBuffer(new BlenderBGRA());
                ImageIO.LoadImageData(imagePathAndFile, imageToOutline);

                int newIndex = asynchMeshesList.Count;

                MarchingSquaresByte marchingSquaresData = new MarchingSquaresByte(imageToOutline, 5, 0);
                marchingSquaresData.CreateLineSegments();
                Polygons lineLoops = marchingSquaresData.CreateLineLoops(1);

                if (lineLoops.Count == 1)
                {
                    continue;
                }

                Polygon boundingPoly = new Polygon();
                IntPoint min = new IntPoint(-1, -1);
                IntPoint max = new IntPoint(imageToOutline.Width + 1, imageToOutline.Height + 1);
                boundingPoly.Add(min);
                boundingPoly.Add(new IntPoint(min.X, max.Y));
                boundingPoly.Add(max);
                boundingPoly.Add(new IntPoint(max.X, min.Y));

                // now clip the polygons to get the inside and outside polys
                Clipper clipper = new Clipper();
                clipper.AddPaths(lineLoops, PolyType.ptSubject, true);
                clipper.AddPath(boundingPoly, PolyType.ptClip, true);

                PolyTree polyTreeForPlate = new PolyTree();
                clipper.Execute(ClipType.ctIntersection, polyTreeForPlate);

                List<Polygons> discreteShapes = new List<Polygons>();
                GetDiscreteShapesRecursive(polyTreeForPlate, discreteShapes);
                foreach (Polygons polygonShape in discreteShapes)
                {
                    PathStorage vectorShape = PlatingHelper.PolygonToPathStorage(polygonShape);

                    Mesh outlineMesh = VertexSourceToMesh.Extrude(vectorShape, 10);

                    if (outlineMesh.Faces.Count > 0)
                    {
                        asynchMeshesList.Add(outlineMesh);

                        PlatingMeshData newMeshInfo = new PlatingMeshData();

                        asynchPlatingDataList.Add(newMeshInfo);
                        asynchMeshTransforms.Add(Matrix4X4.Identity);

                        PlatingHelper.MoveMeshToOpenPosition(newIndex, asynchPlatingDataList, asynchMeshesList, asynchMeshTransforms);
                        PlatingHelper.PlaceMeshOnBed(asynchMeshesList, asynchMeshTransforms, newIndex, false);
                        
                        PlatingHelper.CreateITraceableForMesh(asynchPlatingDataList, asynchMeshesList, asynchMeshesList.Count - 1);
                    }
                }

                PlatingHelper.CenterMeshesXY(asynchMeshesList, asynchMeshTransforms);

                //backgroundWorker.ReportProgress((i + 1) * 95 / currentText.Length);
            }

            //SetWordSize(asynchMeshesList, asynchMeshTransforms);
            //SetWordHeight(asynchMeshesList, asynchMeshTransforms);

            backgroundWorker.ReportProgress(95);
        }

        private void GetDiscreteShapesRecursive(PolyNode polyTreeOfImage, List<Polygons> discreteAreas)
        {
            if (!polyTreeOfImage.IsHole)
            {
                // we have fonud a new polygon, add it and it's holes then recurse into its holes
                Polygons currentShape = new Polygons();
                discreteAreas.Add(currentShape);
                currentShape.Add(polyTreeOfImage.Contour);

                foreach (PolyNode child in polyTreeOfImage.Childs)
                {
                    if (polyTreeOfImage.IsHole)
                    {
                        currentShape.Add(child.Contour);
                    }
                }
            }

            foreach (PolyNode child in polyTreeOfImage.Childs)
            {
                GetDiscreteShapesRecursive(child, discreteAreas);
            }
        }

        void insertOutlinesBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            UnlockEditControls();
            PullMeshDataFromAsynchLists();
            saveButton.Visible = true;
            saveAndExitButton.Visible = true;
            // now set the selection to the new copy
            SelectedMeshIndex = 0;
        }

        private void PushMeshDataToAsynchLists(bool copyTraceInfo)
        {
            asynchMeshesList.Clear();
            asynchMeshTransforms.Clear();
            for (int i = 0; i < Meshes.Count; i++)
            {
                Mesh mesh = Meshes[i];
                asynchMeshesList.Add(new Mesh(mesh));
                asynchMeshTransforms.Add(MeshTransforms[i]);
            }
            asynchPlatingDataList.Clear();
            for (int i = 0; i < MeshExtraData.Count; i++)
            {
                PlatingMeshData meshData = new PlatingMeshData();
                meshData.currentScale = MeshExtraData[i].currentScale;
                if (copyTraceInfo)
                {
                    meshData.traceableData = MeshExtraData[i].traceableData;
                }
                asynchPlatingDataList.Add(meshData);
            }
        }

        void arrangePartsBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            UnlockEditControls();
            saveButton.Visible = true;
            saveAndExitButton.Visible = true;
            partSelectButton.ClickButton(null);

            PullMeshDataFromAsynchLists();
        }

        private void PullMeshDataFromAsynchLists()
        {
            Meshes.Clear();
            foreach (Mesh mesh in asynchMeshesList)
            {
                Meshes.Add(mesh);
            }
            MeshTransforms.Clear();
            foreach (Matrix4X4 transform in asynchMeshTransforms)
            {
                MeshTransforms.Add(transform);
            }
            MeshExtraData.Clear();
            foreach (PlatingMeshData meshData in asynchPlatingDataList)
            {
                MeshExtraData.Add(meshData);
            }
        }

        void meshViewerWidget_LoadDone(object sender, EventArgs e)
        {
            UnlockEditControls();
        }

        void LockEditControls()
        {
            editPlateButtonsContainer.Visible = false;
            buttonRightPanelDisabledCover.Visible = true;
            if (viewControlsSeparator != null)
            {
                viewControlsSeparator.Visible = false;
                partSelectButton.Visible = false;
                if (meshViewerWidget.TrackballTumbleWidget.TransformState == TrackBallController.MouseDownType.None)
                {
                    rotateViewButton.ClickButton(null);
                }
            }
        }

        void UnlockEditControls()
        {
            buttonRightPanelDisabledCover.Visible = false;
            processingProgressControl.Visible = false;

            viewControlsSeparator.Visible = true;
            partSelectButton.Visible = true;
            editPlateButtonsContainer.Visible = true;
        }

        private void DeleteSelectedMesh()
        {
            // don't ever delet the last mesh
            if (Meshes.Count > 1)
            {
                Meshes.RemoveAt(SelectedMeshIndex);
                MeshExtraData.RemoveAt(SelectedMeshIndex);
                MeshTransforms.RemoveAt(SelectedMeshIndex);
                SelectedMeshIndex = Math.Min(SelectedMeshIndex, Meshes.Count - 1);
                saveButton.Visible = true;
                saveAndExitButton.Visible = true;
                Invalidate();
            }
        }

        void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            processingProgressControl.PercentComplete = e.ProgressPercentage;
        }

        private FlowLayoutWidget CreateRightButtonPannel(double buildHeight)
        {
            FlowLayoutWidget buttonRightPanel = new FlowLayoutWidget(FlowDirection.TopToBottom);
            buttonRightPanel.Width = 200;
            {
                BorderDouble buttonMargin = new BorderDouble(top: 3);

                // put in the word editing menu
                {
                    CheckBox expandPictureOptions = expandMenuOptionFactory.GenerateCheckBoxButton("Picture Edit", "icon_arrow_right_no_border_32x32.png", "icon_arrow_down_no_border_32x32.png");
                    expandPictureOptions.Margin = new BorderDouble(bottom: 2);
                    buttonRightPanel.AddChild(expandPictureOptions);

                    FlowLayoutWidget pictureOptionContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                    pictureOptionContainer.HAnchor = HAnchor.ParentLeftRight;
                    pictureOptionContainer.Visible = false;
                    buttonRightPanel.AddChild(pictureOptionContainer);

                    sizeScrollBar = InseretUiForSlider(pictureOptionContainer, "Size:", .3, 2);
                    {
                        sizeScrollBar.ValueChanged += (sender, e) =>
                        {
                            SetWordSize(Meshes, MeshTransforms);
                        };
                    }

                    heightScrollBar = InseretUiForSlider(pictureOptionContainer, "Height:", .05, 1);
                    {
                        heightScrollBar.ValueChanged += (sender, e) =>
                        {
                            SetWordHeight(Meshes, MeshTransforms);
                        };
                    }

                    rotationScrollBar = InseretUiForSlider(pictureOptionContainer, "Rotation:", .5, 1);
                    {
                        rotationScrollBar.ValueChanged += (sender, e) =>
                        {
                            //SetWordSpacing(Meshes, MeshTransforms, MeshPlatingData);
                        };
                    }

                    expandPictureOptions.CheckedStateChanged += (sender, e) =>
                    {
                        pictureOptionContainer.Visible = expandPictureOptions.Checked;
                    };

                    expandPictureOptions.Checked = true;
                }

                // put in the letter editing menu
                {
                    CheckBox expandLetterOptions = expandMenuOptionFactory.GenerateCheckBoxButton("Letter", "icon_arrow_right_no_border_32x32.png", "icon_arrow_down_no_border_32x32.png");
                    expandLetterOptions.Margin = new BorderDouble(bottom: 2);
                    //buttonRightPanel.AddChild(expandLetterOptions);

                    FlowLayoutWidget letterOptionContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                    letterOptionContainer.HAnchor = HAnchor.ParentLeftRight;
                    letterOptionContainer.Visible = false;
                    buttonRightPanel.AddChild(letterOptionContainer);

                    Slider sizeScrollBar = InseretUiForSlider(letterOptionContainer, "Size:");
                    Slider heightScrollBar = InseretUiForSlider(letterOptionContainer, "Height:");
                    Slider rotationScrollBar = InseretUiForSlider(letterOptionContainer, "Rotation:");

                    Button copyButton = whiteButtonFactory.Generate("Copy", centerText: true);
                    letterOptionContainer.AddChild(copyButton);
                    copyButton.Click += (sender, e) =>
                    {
                        MakeCopyOfMesh();
                    };

                    Button deleteButton = whiteButtonFactory.Generate("Delete", centerText: true);
                    deleteButton.Margin = new BorderDouble(left: 20);
                    letterOptionContainer.AddChild(deleteButton);
                    deleteButton.Click += (sender, e) =>
                    {
                        DeleteSelectedMesh();
                    };

                    expandLetterOptions.CheckedStateChanged += (sender, e) =>
                    {
                        letterOptionContainer.Visible = expandLetterOptions.Checked;
                    };
                }

                GuiWidget verticalSpacer = new GuiWidget();
                verticalSpacer.VAnchor = VAnchor.ParentBottomTop;
                buttonRightPanel.AddChild(verticalSpacer);

                saveButton = whiteButtonFactory.Generate("Save", centerText: true);
                saveButton.Visible = false;
                saveButton.Cursor = Cursors.Hand;

                saveAndExitButton =  whiteButtonFactory.Generate("Save & Exit", centerText: true);
                saveAndExitButton.Visible = false;
                saveAndExitButton.Cursor = Cursors.Hand;

                //buttonRightPanel.AddChild(saveButton);
                buttonRightPanel.AddChild(saveAndExitButton);
            }

            buttonRightPanel.Padding = new BorderDouble(6, 6);
            buttonRightPanel.Margin = new BorderDouble(0, 1);
            buttonRightPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;
            buttonRightPanel.VAnchor = VAnchor.ParentBottomTop;

            return buttonRightPanel;
        }

        private void SetWordSize(List<Mesh> meshesList, List<Matrix4X4> meshTransforms)
        {
            if (meshesList.Count > 0)
            {
                for (int meshIndex = 0; meshIndex < meshesList.Count; meshIndex++)
                {
                    // take out the last scale
                    double oldSize = 1.0/lastSizeValue;
                    meshTransforms[meshIndex] *= Matrix4X4.CreateScale(new Vector3(oldSize, oldSize, oldSize));

                    double newSize = sizeScrollBar.Value;
                    meshTransforms[meshIndex] *= Matrix4X4.CreateScale(new Vector3(newSize, newSize, newSize));
                }

                lastSizeValue = sizeScrollBar.Value;
            }
        }

        private void SetWordHeight(List<Mesh> meshesList, List<Matrix4X4> meshTransforms)
        {
            if (meshesList.Count > 0)
            {
                for (int meshIndex = 0; meshIndex < meshesList.Count; meshIndex++)
                {
                    // take out the last scale
                    double oldHeight = lastHeightValue;
                    meshTransforms[meshIndex] *= Matrix4X4.CreateScale(new Vector3(1, 1, 1 / oldHeight));

                    double newHeight = heightScrollBar.Value;
                    meshTransforms[meshIndex] *= Matrix4X4.CreateScale(new Vector3(1, 1, newHeight));
                }

                lastHeightValue = heightScrollBar.Value;
            }
        }

        private static Slider InseretUiForSlider(FlowLayoutWidget wordOptionContainer, string header, double min = 0, double max = .5)
        {
            double scrollBarWidth = 100;
            TextWidget spacingText = new TextWidget(header, textColor: RGBA_Bytes.White);
            spacingText.Margin = new BorderDouble(10, 3, 3, 5);
            spacingText.HAnchor = HAnchor.ParentLeft;
            wordOptionContainer.AddChild(spacingText);
            Slider namedSlider = new Slider(new Vector2(), scrollBarWidth, 0, 1);
            namedSlider.Minimum = min;
            namedSlider.Maximum = max;
            namedSlider.Margin = new BorderDouble(3, 5, 3, 3);
            namedSlider.HAnchor = HAnchor.ParentCenter;
            namedSlider.View.BackgroundColor = new RGBA_Bytes();
            wordOptionContainer.AddChild(namedSlider);

            return namedSlider;
        }

        private void AddOutlineControls(FlowLayoutWidget buttonPanel)
        {
            textImageButtonFactory.FixedWidth = 44;

            FlowLayoutWidget degreesContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
            degreesContainer.HAnchor = HAnchor.ParentLeftRight;
            degreesContainer.Padding = new BorderDouble(5);

            GuiWidget horizontalSpacer = new GuiWidget();
            horizontalSpacer.HAnchor = HAnchor.ParentLeftRight;

            TextWidget degreesLabel = new TextWidget("Degrees:", textColor: RGBA_Bytes.White);
            degreesContainer.AddChild(degreesLabel);
            degreesContainer.AddChild(horizontalSpacer);

            MHNumberEdit degreesControl = new MHNumberEdit(45, pixelWidth: 40, allowNegatives: true, increment: 5, minValue: -360, maxValue: 360);
            degreesControl.VAnchor = Agg.UI.VAnchor.ParentTop;
            degreesContainer.AddChild(degreesControl);

            buttonPanel.AddChild(degreesContainer);

            FlowLayoutWidget rotateButtonContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
            rotateButtonContainer.HAnchor = HAnchor.ParentLeftRight;

            Button rotateZButton = textImageButtonFactory.Generate("", "icon_rotate_32x32.png");
            TextWidget centeredZ = new TextWidget("Z", pointSize: 10, textColor: RGBA_Bytes.White); centeredZ.Margin = new BorderDouble(3, 0, 0, 0); centeredZ.AnchorCenter(); rotateZButton.AddChild(centeredZ);
            rotateButtonContainer.AddChild(rotateZButton);
            rotateZButton.Click += (object sender, MouseEventArgs mouseEvent) =>
            {
                if (SelectedMesh != null)
                {
                    double radians = MathHelper.DegreesToRadians(degreesControl.ActuallNumberEdit.Value);
                    AxisAlignedBoundingBox bounds = SelectedMesh.GetAxisAlignedBoundingBox(SelectedMeshTransform);
                    Vector3 startingCenter = bounds.Center;
                    // move it to the origin so it rotates about it's center
                    Matrix4X4 totalTransfrom = Matrix4X4.CreateTranslation(-startingCenter);
                    // rotate it
                    totalTransfrom *= Matrix4X4.CreateRotationZ(radians);
                    SelectedMeshTransform *= totalTransfrom;
                    // find the new center
                    bounds = SelectedMesh.GetAxisAlignedBoundingBox(SelectedMeshTransform);
                    // and shift it back so the new center is where the old center was
                    SelectedMeshTransform *= Matrix4X4.CreateTranslation(startingCenter - bounds.Center);
                    saveButton.Visible = true;
                    saveAndExitButton.Visible = true;
                    Invalidate();
                }
            };

            buttonPanel.AddChild(rotateButtonContainer);

            buttonPanel.AddChild(generateHorizontalRule());
            textImageButtonFactory.FixedWidth = 0;
        }

        private void SetMeshViewerDisplayTheme()
        {
            meshViewerWidget.TrackballTumbleWidget.RotationHelperCircleColor = ActiveTheme.Instance.PrimaryBackgroundColor;
            if (partSelectButton.Checked)
            {
                meshViewerWidget.PartColor = RGBA_Bytes.White;
            }
            else
            {
                meshViewerWidget.PartColor = ActiveTheme.Instance.PrimaryAccentColor;
            }
            meshViewerWidget.SelectedPartColor = ActiveTheme.Instance.PrimaryAccentColor;
            meshViewerWidget.BuildVolumeColor = new RGBA_Bytes(ActiveTheme.Instance.PrimaryAccentColor.Red0To255, ActiveTheme.Instance.PrimaryAccentColor.Green0To255, ActiveTheme.Instance.PrimaryAccentColor.Blue0To255, 50);
        }

        private GuiWidget generateHorizontalRule()
        {
            GuiWidget horizontalRule = new GuiWidget();
            horizontalRule.Height = 1;
            horizontalRule.Margin = new BorderDouble(0, 1, 0, 3);
            horizontalRule.HAnchor = HAnchor.ParentLeftRight;
            horizontalRule.BackgroundColor = new RGBA_Bytes(255, 255, 255, 200);
            return horizontalRule;
        }

        event EventHandler unregisterEvents;
        private void AddHandlers()
        {
            closeButton.Click += onCloseButton_Click;
            
            saveButton.Click += (sender, e) =>
            {
                MergeAndSavePartsToStl();
            };

            saveAndExitButton.Click += (sender, e) =>
            {
                MergeAndSavePartsToStl();                

            };

            ActiveTheme.Instance.ThemeChanged.RegisterEvent(Instance_ThemeChanged, ref unregisterEvents);
        }

        bool partSelectButtonWasClicked = false;
        private void MergeAndSavePartsToStl()
        {
            if (Meshes.Count > 0)
            {
                partSelectButtonWasClicked = partSelectButton.Checked;

                processingProgressControl.textWidget.Text = "Saving Parts:";
                processingProgressControl.Visible = true;
                processingProgressControl.PercentComplete = 0;
                LockEditControls();

                // we sent the data to the asynch lists but we will not pull it back out (only use it as a temp holder).
                PushMeshDataToAsynchLists(true);

                BackgroundWorker mergeAndSavePartsBackgroundWorker = new BackgroundWorker();
                mergeAndSavePartsBackgroundWorker.WorkerReportsProgress = true;

                mergeAndSavePartsBackgroundWorker.DoWork += new DoWorkEventHandler(mergeAndSavePartsBackgroundWorker_DoWork);
                mergeAndSavePartsBackgroundWorker.ProgressChanged += new ProgressChangedEventHandler(BackgroundWorker_ProgressChanged);
                mergeAndSavePartsBackgroundWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(mergeAndSavePartsBackgroundWorker_RunWorkerCompleted);

                mergeAndSavePartsBackgroundWorker.RunWorkerAsync();
            }
        }

        void mergeAndSavePartsBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            BackgroundWorker backgroundWorker = (BackgroundWorker)sender;
            try
            {
                // push all the transforms into the meshes
                for (int i = 0; i < asynchMeshesList.Count; i++)
                {
                    asynchMeshesList[i].Transform(MeshTransforms[i]);

                    int nextPercent = (i + 1) * 40 / asynchMeshesList.Count;
                    backgroundWorker.ReportProgress(nextPercent);
                }

                Mesh mergedMesh = PlatingHelper.DoMerge(asynchMeshesList, backgroundWorker, 40, 80, true);

                bool overwriteExistingItem = (printItem != null);
                if (!overwriteExistingItem)
                {
                    
                    printItem = new PrintItem();
                    printItem.Commit();
                }

                string fileName = string.Format("UserCreated{0}.stl", printItem.Id);
                string filePath = Path.Combine(ApplicationDataStorage.Instance.ApplicationLibraryDataPath, fileName);
                StlProcessing.Save(mergedMesh, filePath);

                printItem.Name = string.Format("{0}", "Image Outlines - Default Name");
                printItem.FileLocation = System.IO.Path.GetFullPath(filePath);
                printItem.PrintItemCollectionID = PrintLibraryListControl.Instance.LibraryCollection.Id;
                printItem.Commit();


                if (!overwriteExistingItem)
                {
                    printItemWrapper = new PrintItemWrapper(printItem);                    

                    queueItem = new PrintLibraryListItem(printItemWrapper);

                    PrintLibraryListControl.Instance.AddChild(queueItem);
                    PrintLibraryListControl.Instance.Invalidate();
                    PrintLibraryListControl.Instance.SaveLibraryItems();
                }
                else
                {
                    queueItem.Name = printItem.Name;
                    printItemWrapper.OnFileHasChanged();
                    PrintLibraryListControl.Instance.Invalidate();
                    PrintLibraryListControl.Instance.SaveLibraryItems();
                }
            }
            catch (System.UnauthorizedAccessException)
            {
                //Do something special when unauthorized?
                StyledMessageBox.ShowMessageBox("Oops! Unable to save changes.", "Unable to save");
            }
            catch
            {
                StyledMessageBox.ShowMessageBox("Oops! Unable to save changes.", "Unable to save");
            }
        }

        void mergeAndSavePartsBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            //Exit after save
            Close();
            
            //UnlockEditControls();
            //// NOTE: we do not pull the data back out of the asynch lists.
            //saveButton.Visible = false;
            //saveAndExitButton.Visible = false;

            //if (partSelectButtonWasClicked)
            //{
            //    partSelectButton.ClickButton(null);
            //}
        }

        bool scaleQueueMenu_Click()
        {
            return true;
        }

        bool rotateQueueMenu_Click()
        {
            return true;
        }

        private void onCloseButton_Click(object sender, EventArgs e)
        {
            UiThread.RunOnIdle(CloseOnIdle);
        }

        void CloseOnIdle(object state)
        {
            Close();
        }

        public override void OnClosed(EventArgs e)
        {
            if (unregisterEvents != null)
            {
                unregisterEvents(this, null);
            }
            base.OnClosed(e);
        }

        void Instance_ThemeChanged(object sender, EventArgs e)
        {
            SetMeshViewerDisplayTheme();
            Invalidate();
        }
    }
}
