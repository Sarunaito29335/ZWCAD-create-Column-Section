// =============================================================================
// ZWCAD RC COLUMN & LINKS ADD GENERATOR PLUGIN (SINGLE-FILE EDITION FOR ZWCAD)
// =============================================================================
// Required References (Copy Local = False):
// - ZwDatabaseMgd.dll
// - ZwManaged.dll
// Standard .NET Framework References:
// - System
// - System.Drawing
// - System.Windows.Forms
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using CadException = ZwSoft.ZwCAD.Runtime.Exception;
using Font = System.Drawing.Font;
using FlowDirection = System.Windows.Forms.FlowDirection;

[assembly: CommandClass(typeof(ZwCadRcColumnPlugin.PluginCommands))]
[assembly: ExtensionApplication(typeof(ZwCadRcColumnPlugin.PluginCommands))]

namespace ZwCadRcColumnPlugin
{
    // --- From Commands/PluginCommands.cs ---
    /// <summary>
    /// Implements CAD extension lifecycle and registers the RCCOLUMN command.
    /// Supports both Tab 1 (Normal Column) and Tab 2 (Links Add).
    /// </summary>
    public class PluginCommands : IExtensionApplication
    {
        private static ColumnParameters _lastColumnParameters = new ColumnParameters();
        private static LinkParameters _lastLinkParameters = new LinkParameters();

        public void Initialize()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                doc.Editor.WriteMessage("\n[RC Column Generator] Plugin loaded. Type RCCOLUMN to generate an RC column or add links.\n");
            }
        }

        public void Terminate()
        {
        }

        /// <summary>
        /// Command entry point: RCCOLUMN
        /// Opens MainDialog with two tabs:
        /// - Tab 1: Normal Column (original column cross-section generator)
        /// - Tab 2: Links Add (link parameter input -> Link Type Selection -> CAD main bar center picking)
        /// </summary>
        [CommandMethod("RCCOLUMN", CommandFlags.Modal)]
        public void GenerateRcColumnCommand()
        {
            Document doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;

            try
            {
                bool reopenLinksAddTab = false;

                while (true)
                {
                    MainDialogAction action;

                    using (MainDialog mainDialog = new MainDialog(_lastColumnParameters, _lastLinkParameters))
                    {
                        if (reopenLinksAddTab)
                        {
                            mainDialog.SelectLinksAddTab();
                        }

                        DialogResult result = CadApp.ShowModalDialog(mainDialog);
                        if (result != DialogResult.OK)
                        {
                            ed.WriteMessage("\n*RCCOLUMN canceled*\n");
                            return;
                        }

                        action = mainDialog.SelectedAction;
                        _lastColumnParameters = mainDialog.ColumnParameters;
                        _lastLinkParameters = mainDialog.LinkParameters;
                    }

                    if (action == MainDialogAction.GenerateNormalColumn)
                    {
                        ExecuteNormalColumnWorkflow(doc, ed, _lastColumnParameters);
                        return;
                    }

                    if (action == MainDialogAction.ProceedToLinksAdd)
                    {
                        using (LinkTypeSelectionDialog typeDialog = new LinkTypeSelectionDialog(_lastLinkParameters))
                        {
                            DialogResult typeResult = CadApp.ShowModalDialog(typeDialog);

                            if (typeDialog.NavigateBackRequested || typeResult == DialogResult.Retry)
                            {
                                // User clicked "< Back": return to MainDialog on the Links Add tab
                                reopenLinksAddTab = true;
                                continue;
                            }

                            if (typeResult != DialogResult.OK)
                            {
                                ed.WriteMessage("\n*Links Add canceled*\n");
                                return;
                            }

                            _lastLinkParameters.SelectedLinkType = typeDialog.SelectedLinkType;
                        }

                        LinkCommandHandler.ExecuteLinkPlacementWorkflow(doc, _lastLinkParameters);
                        return;
                    }

                    return;
                }
            }
            catch (CadException cadEx)
            {
                ed.WriteMessage($"\n[CAD API Error] {cadEx.Message} (ErrorStatus: {cadEx.ErrorStatus})\n");
                MessageBox.Show(
                    $"A CAD API error occurred:\n{cadEx.Message}",
                    "CAD API Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] {ex.Message}\n");
                MessageBox.Show(
                    $"An unexpected error occurred:\n{ex.Message}",
                    "RC Column Generator Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void ExecuteNormalColumnWorkflow(Document doc, Editor ed, ColumnParameters parameters)
        {
            PromptPointOptions ptOptions = new PromptPointOptions("\nSpecify insertion point (bottom-left corner of column): ")
            {
                AllowNone = false
            };

            PromptPointResult ptResult = ed.GetPoint(ptOptions);
            if (ptResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n*Insertion point not specified. Command canceled.*\n");
                return;
            }

            Point3d wcsPoint = ptResult.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            Point2D insertionPoint = new Point2D(wcsPoint.X, wcsPoint.Y);

            ColumnSectionGeometry geometry = ColumnGeometryCalculator.Calculate(parameters, insertionPoint);
            ZwCadColumnDrawer.DrawColumnSection(doc, geometry);

            ed.WriteMessage(
                $"\nRC Column ({parameters.WidthB} x {parameters.HeightH} mm, " +
                $"User Cover = {parameters.UserInputCover} mm, Effective Cover = {parameters.EffectiveCover} mm) " +
                $"generated with {geometry.LongitudinalBars.Count} longitudinal bars.\n");
        }
    }

    // --- From Models/ColumnParameters.cs ---
    /// <summary>
    /// Holds user-defined and derived geometric parameters for the RC rectangular column cross-section.
    /// All linear dimensions are in millimeters (1 drawing unit = 1 mm).
    /// </summary>
    public class ColumnParameters
    {
        /// <summary>
        /// Fixed additional concrete cover offset (10 mm) applied to all longitudinal reinforcement bars.
        /// </summary>
        public const double AdditionalCoverOffsetMm = 10.0;

        /// <summary>
        /// Fixed dimension text height in drawing units (mm).
        /// </summary>
        public const double DimensionTextHeight = 20.0;

        /// <summary>
        /// Overall column height h (mm). Default = 400 mm.
        /// </summary>
        public double HeightH { get; set; } = 400.0;

        /// <summary>
        /// Overall column width b (mm). Default = 200 mm.
        /// </summary>
        public double WidthB { get; set; } = 200.0;

        /// <summary>
        /// Diameter of longitudinal corner reinforcement bars (mm). Default = 20 mm.
        /// </summary>
        public double CornerBarDiameter { get; set; } = 20.0;

        /// <summary>
        /// Number of additional middle reinforcement bars along the horizontal direction (X-X) per face. Default = 1.
        /// </summary>
        public int MiddleBarsCountX { get; set; } = 1;

        /// <summary>
        /// Diameter of horizontal middle reinforcement bars along X-X (mm). Default = 20 mm.
        /// </summary>
        public double MiddleBarDiameterX { get; set; } = 20.0;

        /// <summary>
        /// Number of additional middle reinforcement bars along the vertical direction (Y-Y) per face. Default = 1.
        /// </summary>
        public int MiddleBarsCountY { get; set; } = 1;

        /// <summary>
        /// Diameter of vertical middle reinforcement bars along Y-Y (mm). Default = 20 mm.
        /// </summary>
        public double MiddleBarDiameterY { get; set; } = 20.0;

        /// <summary>
        /// User-input concrete cover measured to the outer surface of main reinforcement bars (mm). Default = 30 mm.
        /// </summary>
        public double UserInputCover { get; set; } = 30.0;

        /// <summary>
        /// Effective cover used internally for all reinforcement and stirrup calculations:
        /// Effective Cover = User Input Cover + 10 mm.
        /// Computed exactly once here to prevent duplicate offset additions.
        /// </summary>
        public double EffectiveCover => UserInputCover + AdditionalCoverOffsetMm;

        /// <summary>
        /// Validates all parameters for geometric feasibility and physical constraints.
        /// </summary>
        /// <param name="errors">List of human-readable validation error messages.</param>
        /// <returns>True if all parameters are valid; otherwise false.</returns>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (HeightH <= 0)
            {
                errors.Add("Overall column height h (mm) must be greater than 0.");
            }

            if (WidthB <= 0)
            {
                errors.Add("Overall column width b (mm) must be greater than 0.");
            }

            if (CornerBarDiameter <= 0)
            {
                errors.Add("Diameter of corner bars (mm) must be greater than 0.");
            }

            if (MiddleBarsCountX < 0)
            {
                errors.Add("Number of middle bars about X-X cannot be negative.");
            }

            if (MiddleBarDiameterX <= 0)
            {
                errors.Add("Dia of middle bars about X-X (mm) must be greater than 0.");
            }

            if (MiddleBarsCountY < 0)
            {
                errors.Add("Number of middle bars about Y-Y cannot be negative.");
            }

            if (MiddleBarDiameterY <= 0)
            {
                errors.Add("Dia of middle bars about Y-Y (mm) must be greater than 0.");
            }

            if (UserInputCover < 0)
            {
                errors.Add("Cover on main bars (mm) cannot be negative.");
            }

            // Geometric feasibility checks if basic positive checks pass
            if (errors.Count == 0)
            {
                double cornerCenterOffset = EffectiveCover + (CornerBarDiameter / 2.0);
                double availableCenterWidth = WidthB - (2.0 * cornerCenterOffset);
                double availableCenterHeight = HeightH - (2.0 * cornerCenterOffset);

                if (availableCenterWidth <= CornerBarDiameter)
                {
                    errors.Add(
                        $"Column width b ({WidthB} mm) is too small to accommodate the effective cover ({EffectiveCover} mm) " +
                        $"and two corner bars of diameter {CornerBarDiameter} mm.");
                }

                if (availableCenterHeight <= CornerBarDiameter)
                {
                    errors.Add(
                        $"Column height h ({HeightH} mm) is too small to accommodate the effective cover ({EffectiveCover} mm) " +
                        $"and two corner bars of diameter {CornerBarDiameter} mm.");
                }

                if (MiddleBarsCountX > 0 && availableCenterWidth > 0)
                {
                    double spacingX = availableCenterWidth / (MiddleBarsCountX + 1);
                    double minRequiredSpacingX = ( Math.Max(CornerBarDiameter, MiddleBarDiameterX) + MiddleBarDiameterX ) / 2.0;
                    if (spacingX <= minRequiredSpacingX)
                    {
                        errors.Add(
                            $"Horizontal middle bars ({MiddleBarsCountX} bars of {MiddleBarDiameterX} mm) overlap along width b ({WidthB} mm). " +
                            "Reduce bar count/diameter or increase column width.");
                    }
                }

                if (MiddleBarsCountY > 0 && availableCenterHeight > 0)
                {
                    double spacingY = availableCenterHeight / (MiddleBarsCountY + 1);
                    double minRequiredSpacingY = ( Math.Max(CornerBarDiameter, MiddleBarDiameterY) + MiddleBarDiameterY ) / 2.0;
                    if (spacingY <= minRequiredSpacingY)
                    {
                        errors.Add(
                            $"Vertical middle bars ({MiddleBarsCountY} bars of {MiddleBarDiameterY} mm) overlap along height h ({HeightH} mm). " +
                            "Reduce bar count/diameter or increase column height.");
                    }
                }
            }

            return errors.Count == 0;
        }
    }

    // --- From Models/LinkParameters.cs ---
    /// <summary>
    /// Enumeration of the three supported reinforcement link types in the "Links Add" feature.
    /// </summary>
    public enum LinkType
    {
        OneLeg = 1,
        TwoLegsOuter = 2,
        TwoLegsInner = 3
    }

    /// <summary>
    /// Holds user-defined parameters for the "Links Add" feature.
    /// All dimensions are in millimeters (1 drawing unit = 1 mm).
    /// </summary>
    public class LinkParameters
    {
        /// <summary>
        /// Fixed hook (bent-end) extension length in drawing units (mm).
        /// Mandatory requirement: Hook Length = 50 mm.
        /// </summary>
        public const double HookLengthMm = 50.0;

        /// <summary>
        /// Diameter of the reinforcement link / stirrup (mm). Default = 10 mm.
        /// </summary>
        public double LinkDiameter { get; set; } = 10.0;

        /// <summary>
        /// Diameter of the longitudinal main reinforcement bars (mm). Default = 20 mm.
        /// </summary>
        public double MainBarDiameter { get; set; } = 20.0;

        /// <summary>
        /// Selected link arrangement type.
        /// </summary>
        public LinkType SelectedLinkType { get; set; } = LinkType.OneLeg;

        /// <summary>
        /// Common geometric offset distance from the selected main bar center to the link centerline:
        /// Offset Distance = (Main Bar Diameter / 2) + (Link Diameter / 2).
        /// For Main Bar = 20 mm and Link = 10 mm, Offset Distance = 10 + 5 = 15 mm.
        /// </summary>
        public double OffsetDistance => (MainBarDiameter / 2.0) + (LinkDiameter / 2.0);

        /// <summary>
        /// Validates the link parameters.
        /// </summary>
        public bool Validate(out List<string> errors)
        {
            errors = new List<string>();

            if (LinkDiameter <= 0 || double.IsNaN(LinkDiameter) || double.IsInfinity(LinkDiameter))
            {
                errors.Add("Link Diameter (mm) must be a positive number greater than 0.");
            }

            if (MainBarDiameter <= 0 || double.IsNaN(MainBarDiameter) || double.IsInfinity(MainBarDiameter))
            {
                errors.Add("Main Bar Diameter (mm) must be a positive number greater than 0.");
            }

            return errors.Count == 0;
        }
    }

    // --- From Geometry/ColumnGeometryCalculator.cs ---
    /// <summary>
    /// Represents a 2D point in Model Space coordinates (X, Y in mm).
    /// </summary>
    public struct Point2D
    {
        public double X { get; }
        public double Y { get; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Represents a 2D polyline vertex with an optional arc bulge value.
    /// - Bulge = 0.0: straight segment to the next vertex.
    /// - Bulge = tan(includedAngle / 4.0): circular arc segment to the next vertex
    ///   (positive = counter-clockwise, negative = clockwise).
    /// </summary>
    public struct PolylineVertex2D
    {
        public double X { get; }
        public double Y { get; }
        public double Bulge { get; }

        public PolylineVertex2D(double x, double y, double bulge = 0.0)
        {
            X = x;
            Y = y;
            Bulge = bulge;
        }
    }

    /// <summary>
    /// Represents a circular longitudinal reinforcement bar to be drawn in Model Space.
    /// </summary>
    public class ReinforcingBarGeometry
    {
        public Point2D Center { get; }
        public double Diameter { get; }
        public double Radius => Diameter / 2.0;
        public string BarRole { get; }

        public ReinforcingBarGeometry(Point2D center, double diameter, string barRole)
        {
            Center = center;
            Diameter = diameter;
            BarRole = barRole;
        }
    }

    /// <summary>
    /// Represents a linear dimension specification.
    /// </summary>
    public class DimensionSpec
    {
        public Point2D ExtensionLine1Point { get; set; }
        public Point2D ExtensionLine2Point { get; set; }
        public Point2D DimensionLinePoint { get; set; }
        public double RotationRadians { get; set; }
        public double TextHeight { get; set; }
    }

    /// <summary>
    /// Holds the complete calculated geometry for the RC column cross-section.
    /// </summary>
    public class ColumnSectionGeometry
    {
        public IReadOnlyList<PolylineVertex2D> OuterConcreteVertices { get; set; }
        public IReadOnlyList<PolylineVertex2D> InnerStirrupVertices { get; set; }
        public IReadOnlyList<ReinforcingBarGeometry> LongitudinalBars { get; set; }
        public DimensionSpec HorizontalWidthDimension { get; set; }
        public DimensionSpec VerticalHeightDimension { get; set; }
    }

    /// <summary>
    /// Pure geometric calculator that computes coordinates for the concrete boundary,
    /// rounded-corner stirrup boundary, reinforcement bar centers, and dimension lines.
    /// </summary>
    public static class ColumnGeometryCalculator
    {
        private const double DefaultDimensionLineOffsetMm = 50.0;
        private static readonly double Bulge90Deg = Math.Sqrt(2.0) - 1.0;

        public static ColumnSectionGeometry Calculate(ColumnParameters parameters, Point2D insertionPoint)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (!parameters.Validate(out List<string> errors))
                throw new InvalidOperationException("Invalid column parameters:\n" + string.Join("\n", errors));

            double x0 = insertionPoint.X;
            double y0 = insertionPoint.Y;
            double b = parameters.WidthB;
            double h = parameters.HeightH;

            // EffectiveCover = UserInputCover + 10 mm (computed once inside ColumnParameters.EffectiveCover)
            double effectiveCover = parameters.EffectiveCover;

            // 1. Outer Concrete Boundary (CCW from bottom-left, sharp 90-degree corners)
            var outerVertices = new List<PolylineVertex2D>
            {
                new PolylineVertex2D(x0, y0, 0.0),
                new PolylineVertex2D(x0 + b, y0, 0.0),
                new PolylineVertex2D(x0 + b, y0 + h, 0.0),
                new PolylineVertex2D(x0, y0 + h, 0.0)
            };

            // 2. Corner bar positions
            double cornerDia = parameters.CornerBarDiameter;
            double cornerRadius = cornerDia / 2.0;
            double cornerOffset = effectiveCover + cornerRadius;

            double xCornerLeft = x0 + cornerOffset;
            double xCornerRight = x0 + b - cornerOffset;
            double yCornerBottom = y0 + cornerOffset;
            double yCornerTop = y0 + h - cornerOffset;

            // 3. Inner Stirrup / Tie Boundary with smooth 90-degree curved corners wrapping the 4 corner bars
            double sLeft = x0 + effectiveCover;
            double sRight = x0 + b - effectiveCover;
            double sBottom = y0 + effectiveCover;
            double sTop = y0 + h - effectiveCover;

            var stirrupVertices = new List<PolylineVertex2D>
            {
                new PolylineVertex2D(xCornerLeft, sBottom, 0.0),
                new PolylineVertex2D(xCornerRight, sBottom, Bulge90Deg),
                new PolylineVertex2D(sRight, yCornerBottom, 0.0),
                new PolylineVertex2D(sRight, yCornerTop, Bulge90Deg),
                new PolylineVertex2D(xCornerRight, sTop, 0.0),
                new PolylineVertex2D(xCornerLeft, sTop, Bulge90Deg),
                new PolylineVertex2D(sLeft, yCornerTop, 0.0),
                new PolylineVertex2D(sLeft, yCornerBottom, Bulge90Deg)
            };

            // 4. Longitudinal Reinforcement Bars
            var bars = new List<ReinforcingBarGeometry>
            {
                new ReinforcingBarGeometry(new Point2D(xCornerLeft, yCornerBottom), cornerDia, "Corner-BottomLeft"),
                new ReinforcingBarGeometry(new Point2D(xCornerRight, yCornerBottom), cornerDia, "Corner-BottomRight"),
                new ReinforcingBarGeometry(new Point2D(xCornerRight, yCornerTop), cornerDia, "Corner-TopRight"),
                new ReinforcingBarGeometry(new Point2D(xCornerLeft, yCornerTop), cornerDia, "Corner-TopLeft")
            };

            // Middle Bars along X-X (Horizontal direction: Top and Bottom faces)
            int countX = parameters.MiddleBarsCountX;
            if (countX > 0)
            {
                double diaX = parameters.MiddleBarDiameterX;
                double offsetXFace = effectiveCover + (diaX / 2.0);

                double yBottomBar = y0 + offsetXFace;
                double yTopBar = y0 + h - offsetXFace;
                double stepX = (xCornerRight - xCornerLeft) / (countX + 1);

                for (int i = 1; i <= countX; i++)
                {
                    double xMid = xCornerLeft + (i * stepX);
                    bars.Add(new ReinforcingBarGeometry(new Point2D(xMid, yBottomBar), diaX, $"MiddleX-Bottom-{i}"));
                    bars.Add(new ReinforcingBarGeometry(new Point2D(xMid, yTopBar), diaX, $"MiddleX-Top-{i}"));
                }
            }

            // Middle Bars along Y-Y (Vertical direction: Left and Right faces)
            int countY = parameters.MiddleBarsCountY;
            if (countY > 0)
            {
                double diaY = parameters.MiddleBarDiameterY;
                double offsetYFace = effectiveCover + (diaY / 2.0);

                double xLeftBar = x0 + offsetYFace;
                double xRightBar = x0 + b - offsetYFace;
                double stepY = (yCornerTop - yCornerBottom) / (countY + 1);

                for (int j = 1; j <= countY; j++)
                {
                    double yMid = yCornerBottom + (j * stepY);
                    bars.Add(new ReinforcingBarGeometry(new Point2D(xLeftBar, yMid), diaY, $"MiddleY-Left-{j}"));
                    bars.Add(new ReinforcingBarGeometry(new Point2D(xRightBar, yMid), diaY, $"MiddleY-Right-{j}"));
                }
            }

            // 5. Dimension Lines
            var horizontalDim = new DimensionSpec
            {
                ExtensionLine1Point = new Point2D(x0, y0 + h),
                ExtensionLine2Point = new Point2D(x0 + b, y0 + h),
                DimensionLinePoint = new Point2D(x0 + (b / 2.0), y0 + h + DefaultDimensionLineOffsetMm),
                RotationRadians = 0.0,
                TextHeight = ColumnParameters.DimensionTextHeight
            };

            var verticalDim = new DimensionSpec
            {
                ExtensionLine1Point = new Point2D(x0, y0),
                ExtensionLine2Point = new Point2D(x0, y0 + h),
                DimensionLinePoint = new Point2D(x0 - DefaultDimensionLineOffsetMm, y0 + (h / 2.0)),
                RotationRadians = Math.PI / 2.0,
                TextHeight = ColumnParameters.DimensionTextHeight
            };

            return new ColumnSectionGeometry
            {
                OuterConcreteVertices = outerVertices,
                InnerStirrupVertices = stirrupVertices,
                LongitudinalBars = bars,
                HorizontalWidthDimension = horizontalDim,
                VerticalHeightDimension = verticalDim
            };
        }
    }

    // --- From Geometry/LinkGeometryCalculator.cs ---
    /// <summary>
    /// Represents the calculated polyline geometry (with curved bends around bars) for a reinforcement link.
    /// </summary>
    public class LinkPolylineGeometry
    {
        public IReadOnlyList<PolylineVertex2D> Vertices { get; set; }
        public bool IsClosed { get; set; }
        public double OffsetUsed { get; set; }
        public double HookLengthUsed { get; set; }
    }

    /// <summary>
    /// Calculates the exact 2D polyline vertices and arc bulges for all three reinforcement link types:
    /// 1. 1 Leg Type (2 selected main bar centers, 180-degree curved bends around both bars + 50 mm hooks on both ends)
    /// 2. 2 Legs Type - Outer (2 selected main bar centers: top-left and bottom-right, 90-degree curved bends + 50 mm hooks at upper-right)
    /// 3. 2 Legs Type - Inner (4 selected main bar centers, tangent curved bends around all 4 bars + 50 mm hooks at 1st bar)
    /// </summary>
    public static class LinkGeometryCalculator
    {
        private const double MinPointDistanceTolerance = 1e-3;
        private static readonly double Bulge90Deg = Math.Sqrt(2.0) - 1.0;
        private static readonly double Bulge135Deg = Math.Tan((3.0 * Math.PI / 4.0) / 4.0);

        public static LinkPolylineGeometry Calculate(LinkParameters parameters, IReadOnlyList<Point2D> selectedBarCenters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (selectedBarCenters == null)
                throw new ArgumentNullException(nameof(selectedBarCenters));

            if (!parameters.Validate(out List<string> errors))
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            switch (parameters.SelectedLinkType)
            {
                case LinkType.OneLeg:
                    if (selectedBarCenters.Count != 2)
                        throw new ArgumentException("1 Leg Type requires exactly 2 selected main bar center points.");
                    return CalculateOneLegLink(parameters, selectedBarCenters[0], selectedBarCenters[1]);

                case LinkType.TwoLegsOuter:
                    if (selectedBarCenters.Count != 2)
                        throw new ArgumentException("2 Legs Type - Outer requires exactly 2 selected main bar center points.");
                    return CalculateTwoLegsOuterLink(parameters, selectedBarCenters[0], selectedBarCenters[1]);

                case LinkType.TwoLegsInner:
                    if (selectedBarCenters.Count != 4)
                        throw new ArgumentException("2 Legs Type - Inner requires exactly 4 selected main bar center points.");
                    return CalculateTwoLegsInnerLink(
                        parameters,
                        selectedBarCenters[0],
                        selectedBarCenters[1],
                        selectedBarCenters[2],
                        selectedBarCenters[3]);

                default:
                    throw new NotSupportedException($"Unsupported link type: {parameters.SelectedLinkType}");
            }
        }

        /// <summary>
        /// Link Type 1: 1 Leg Type (Single-leg link between 2 main bars P1 and P2).
        /// Matches reference image:
        /// - Offset d = (MainBarDia / 2) + (LinkDia / 2).
        /// - Wraps around BOTH P1 and P2 with smooth 180-degree semicircular arcs of radius d (Bulge = -1.0)
        ///   and includes a 50 mm inward hook at BOTH ends.
        /// - Reversing the click order (P2 -> P1) flips the main leg and hooks to the opposite side.
        /// </summary>
        public static LinkPolylineGeometry CalculateOneLegLink(LinkParameters parameters, Point2D p1, Point2D p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length < MinPointDistanceTolerance)
            {
                throw new InvalidOperationException("The two selected main bar centers cannot be identical.");
            }

            double d = parameters.OffsetDistance;
            double hookLen = LinkParameters.HookLengthMm;

            double ux = dx / length;
            double uy = dy / length;
            double nx = -uy;
            double ny = ux;

            // Vertex 0: 50 mm hook tip at P1 end (pointing inward toward P2 on -n side)
            PolylineVertex2D v0 = new PolylineVertex2D(
                p1.X - (d * nx) + (hookLen * ux),
                p1.Y - (d * ny) + (hookLen * uy),
                0.0);

            // Vertex 1: Start of 180-degree clockwise semicircle around outer (-u) side of P1 (bulge = -1.0)
            PolylineVertex2D v1 = new PolylineVertex2D(
                p1.X - (d * nx),
                p1.Y - (d * ny),
                -1.0);

            // Vertex 2: End of semicircle at P1 / start of main straight leg on +n side (bulge = 0.0)
            PolylineVertex2D v2 = new PolylineVertex2D(
                p1.X + (d * nx),
                p1.Y + (d * ny),
                0.0);

            // Vertex 3: End of main straight leg at P2 / start of 180-degree clockwise semicircle around outer (+u) side of P2 (bulge = -1.0)
            PolylineVertex2D v3 = new PolylineVertex2D(
                p2.X + (d * nx),
                p2.Y + (d * ny),
                -1.0);

            // Vertex 4: End of semicircle at P2 / start of 50 mm hook on -n side (bulge = 0.0)
            PolylineVertex2D v4 = new PolylineVertex2D(
                p2.X - (d * nx),
                p2.Y - (d * ny),
                0.0);

            // Vertex 5: 50 mm hook tip at P2 end (pointing inward toward P1 on -n side)
            PolylineVertex2D v5 = new PolylineVertex2D(
                p2.X - (d * nx) - (hookLen * ux),
                p2.Y - (d * ny) - (hookLen * uy),
                0.0);

            return new LinkPolylineGeometry
            {
                Vertices = new List<PolylineVertex2D> { v0, v1, v2, v3, v4, v5 },
                IsClosed = false,
                OffsetUsed = d,
                HookLengthUsed = hookLen
            };
        }

        /// <summary>
        /// Link Type 2: 2 Legs Type - Outer.
        /// - User selects P1 (top-left main bar center) and P2 (bottom-right main bar center).
        /// - Wraps around all 4 corner bars with smooth circular arcs of radius d = (MainBarDia/2) + (LinkDia/2).
        /// - Includes 135-degree 50 mm seismic hooks at the upper-right corner bar (in reference orientation).
        /// - Reversing the click order reverses the orientation and moves the hooks to the opposite corner.
        /// </summary>
        public static LinkPolylineGeometry CalculateTwoLegsOuterLink(LinkParameters parameters, Point2D p1, Point2D p2)
        {
            double dx = p2.X - p1.X;
            double dy = p1.Y - p2.Y;
            double dist = Math.Sqrt((dx * dx) + (dy * dy));

            if (dist < MinPointDistanceTolerance)
            {
                throw new InvalidOperationException("The two selected main bar centers cannot be identical.");
            }

            if (Math.Abs(dx) < MinPointDistanceTolerance || Math.Abs(dy) < MinPointDistanceTolerance)
            {
                throw new InvalidOperationException(
                    "For '2 Legs Type - Outer', please select two diagonally opposite main bar centers (top-left and bottom-right).");
            }

            double d = parameters.OffsetDistance;
            double hookLen = LinkParameters.HookLengthMm;

            double sx = dx >= 0 ? 1.0 : -1.0;
            double sy = dy >= 0 ? 1.0 : -1.0;

            double orientSign = sx * sy;
            double arcBulge90 = orientSign * Bulge90Deg;
            double arcBulge135 = orientSign * Bulge135Deg;

            double x1 = p1.X;
            double y1 = p1.Y;
            double x2 = p2.X;
            double y2 = p2.Y;

            double cos45 = Math.Sqrt(0.5);

            // 1st 50 mm hook tangent to Upper-Right bar (x2, y1) at -45 deg:
            double tStartArcX = x2 + (sx * d * cos45);
            double tStartArcY = y1 - (sy * d * cos45);
            PolylineVertex2D vHook1Tip = new PolylineVertex2D(
                tStartArcX - (sx * hookLen * cos45),
                tStartArcY - (sy * hookLen * cos45),
                0.0);

            PolylineVertex2D vTR_In = new PolylineVertex2D(tStartArcX, tStartArcY, arcBulge135);
            PolylineVertex2D vTR_Top = new PolylineVertex2D(x2, y1 + (sy * d), 0.0);

            // Top-Left bar (x1, y1)
            PolylineVertex2D vTL_Top = new PolylineVertex2D(x1, y1 + (sy * d), arcBulge90);
            PolylineVertex2D vTL_Left = new PolylineVertex2D(x1 - (sx * d), y1, 0.0);

            // Bottom-Left bar (x1, y2)
            PolylineVertex2D vBL_Left = new PolylineVertex2D(x1 - (sx * d), y2, arcBulge90);
            PolylineVertex2D vBL_Bot = new PolylineVertex2D(x1, y2 - (sy * d), 0.0);

            // Bottom-Right bar (x2, y2)
            PolylineVertex2D vBR_Bot = new PolylineVertex2D(x2, y2 - (sy * d), arcBulge90);
            PolylineVertex2D vBR_Right = new PolylineVertex2D(x2 + (sx * d), y2, 0.0);

            // Return to Upper-Right bar (x2, y1): 135-degree arc to +135 deg tangent + 2nd 50 mm hook
            PolylineVertex2D vTR_Right = new PolylineVertex2D(x2 + (sx * d), y1, arcBulge135);
            double tEndArcX = x2 - (sx * d * cos45);
            double tEndArcY = y1 + (sy * d * cos45);
            PolylineVertex2D vTR_EndArc = new PolylineVertex2D(tEndArcX, tEndArcY, 0.0);

            PolylineVertex2D vHook2Tip = new PolylineVertex2D(
                tEndArcX - (sx * hookLen * cos45),
                tEndArcY - (sy * hookLen * cos45),
                0.0);

            return new LinkPolylineGeometry
            {
                Vertices = new List<PolylineVertex2D>
                {
                    vHook1Tip,
                    vTR_In,
                    vTR_Top,
                    vTL_Top,
                    vTL_Left,
                    vBL_Left,
                    vBL_Bot,
                    vBR_Bot,
                    vBR_Right,
                    vTR_Right,
                    vTR_EndArc,
                    vHook2Tip
                },
                IsClosed = false,
                OffsetUsed = d,
                HookLengthUsed = hookLen
            };
        }

        /// <summary>
        /// Link Type 3: 2 Legs Type - Inner.
        /// - User selects 4 main bar centers sequentially: P1 -> P2 -> P3 -> P4.
        /// - Offsets each edge outward from the 4-bar centroid by d = (MainBarDia/2) + (LinkDia/2),
        ///   connecting tangent points around each bar P_k with a smooth circular arc of radius d.
        /// - Generates 50 mm tangent hooks strictly at the FIRST selected point (P1).
        /// </summary>
        public static LinkPolylineGeometry CalculateTwoLegsInnerLink(
            LinkParameters parameters,
            Point2D p1,
            Point2D p2,
            Point2D p3,
            Point2D p4)
        {
            Point2D[] pts = { p1, p2, p3, p4 };

            for (int i = 0; i < 4; i++)
            {
                Point2D a = pts[i];
                Point2D b = pts[(i + 1) % 4];
                double edgeLen = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                if (edgeLen < MinPointDistanceTolerance)
                {
                    throw new InvalidOperationException("Consecutive selected main bar centers cannot be identical.");
                }
            }

            double d = parameters.OffsetDistance;
            double hookLen = LinkParameters.HookLengthMm;

            double cx = (p1.X + p2.X + p3.X + p4.X) / 4.0;
            double cy = (p1.Y + p2.Y + p3.Y + p4.Y) / 4.0;

            Point2D[] edgeOutNormal = new Point2D[4];

            for (int i = 0; i < 4; i++)
            {
                Point2D a = pts[i];
                Point2D b = pts[(i + 1) % 4];
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;
                double len = Math.Sqrt((ex * ex) + (ey * ey));
                double ux = ex / len;
                double uy = ey / len;

                double nx = -uy;
                double ny = ux;
                double midX = 0.5 * (a.X + b.X);
                double midY = 0.5 * (a.Y + b.Y);
                if (((nx * (midX - cx)) + (ny * (midY - cy))) < 0)
                {
                    nx = -nx;
                    ny = -ny;
                }

                edgeOutNormal[i] = new Point2D(nx, ny);
            }

            Point2D n3 = edgeOutNormal[3];
            Point2D n0 = edgeOutNormal[0];

            // Determine traversal orientation at P1 (+1 for CCW, -1 for CW)
            double cross0 = (n3.X * n0.Y) - (n3.Y * n0.X);
            double s = cross0 >= 0 ? 1.0 : -1.0;

            // Rotate normals by 45 degrees at P1 so both 50 mm hooks are tangent to the circle of radius d at P1
            // and extend diagonally into the core without intersecting bar P1
            double angExtra = s * (Math.PI / 4.0);
            double ca = Math.Cos(angExtra);
            double sa = Math.Sin(angExtra);

            // Start hook normal (45 degrees before n3) and inward tangent vector
            Point2D nh1 = new Point2D((n3.X * ca) + (n3.Y * sa), (-n3.X * sa) + (n3.Y * ca));
            Point2D th1 = new Point2D(-s * (-nh1.Y), -s * nh1.X);

            // End hook normal (45 degrees past n0) and inward tangent vector
            Point2D nh2 = new Point2D((n0.X * ca) - (n0.Y * sa), (n0.X * sa) + (n0.Y * ca));
            Point2D th2 = new Point2D(s * (-nh2.Y), s * nh2.X);

            var vertices = new List<PolylineVertex2D>();

            // 1st 50 mm hook at P1 -> arc around P1 to outgoing tangent t0_out
            Point2D tH1 = new Point2D(pts[0].X + (d * nh1.X), pts[0].Y + (d * nh1.Y));
            Point2D h1Tip = new Point2D(tH1.X + (hookLen * th1.X), tH1.Y + (hookLen * th1.Y));
            Point2D t0Out = new Point2D(pts[0].X + (d * n0.X), pts[0].Y + (d * n0.Y));
            double bulgeStart = ComputeArcBulge(nh1, n0);

            vertices.Add(new PolylineVertex2D(h1Tip.X, h1Tip.Y, 0.0));
            vertices.Add(new PolylineVertex2D(tH1.X, tH1.Y, bulgeStart));
            vertices.Add(new PolylineVertex2D(t0Out.X, t0Out.Y, 0.0));

            // Bars P2 (k=1), P3 (k=2), P4 (k=3)
            for (int k = 1; k < 4; k++)
            {
                Point2D nIn = edgeOutNormal[(k + 3) % 4];
                Point2D nOut = edgeOutNormal[k];
                Point2D tkIn = new Point2D(pts[k].X + (d * nIn.X), pts[k].Y + (d * nIn.Y));
                Point2D tkOut = new Point2D(pts[k].X + (d * nOut.X), pts[k].Y + (d * nOut.Y));
                double bulgeK = ComputeArcBulge(nIn, nOut);

                vertices.Add(new PolylineVertex2D(tkIn.X, tkIn.Y, bulgeK));
                vertices.Add(new PolylineVertex2D(tkOut.X, tkOut.Y, 0.0));
            }

            // Return to P1: incoming tangent t0_in -> arc around P1 to tH2 -> 2nd 50 mm hook at P1
            Point2D t0In = new Point2D(pts[0].X + (d * n3.X), pts[0].Y + (d * n3.Y));
            Point2D tH2 = new Point2D(pts[0].X + (d * nh2.X), pts[0].Y + (d * nh2.Y));
            Point2D h2Tip = new Point2D(tH2.X + (hookLen * th2.X), tH2.Y + (hookLen * th2.Y));
            double bulgeEnd = ComputeArcBulge(n3, nh2);

            vertices.Add(new PolylineVertex2D(t0In.X, t0In.Y, bulgeEnd));
            vertices.Add(new PolylineVertex2D(tH2.X, tH2.Y, 0.0));
            vertices.Add(new PolylineVertex2D(h2Tip.X, h2Tip.Y, 0.0));

            return new LinkPolylineGeometry
            {
                Vertices = vertices,
                IsClosed = false,
                OffsetUsed = d,
                HookLengthUsed = hookLen
            };
        }

        private static double ComputeArcBulge(Point2D n1, Point2D n2)
        {
            double cross = (n1.X * n2.Y) - (n1.Y * n2.X);
            double dot = (n1.X * n2.X) + (n1.Y * n2.Y);
            double deltaTheta = Math.Atan2(cross, dot);
            return Math.Tan(deltaTheta / 4.0);
        }
    }

    // --- From UI/MainDialog.cs ---
    /// <summary>
    /// Enumeration indicating which workflow the user triggered from MainDialog.
    /// </summary>
    public enum MainDialogAction
    {
        None = 0,
        GenerateNormalColumn = 1,
        ProceedToLinksAdd = 2
    }

    /// <summary>
    /// Main dialog displayed by the RCCOLUMN command containing two tabs:
    /// - Tab 1: Normal Column (preserves all original column parameters and generation behavior)
    /// - Tab 2: Links Add (inputs Link Diameter and Main Bar Diameter, then proceeds to Link Type Selection)
    /// Uses TableLayoutPanel + FlowLayoutPanel so Generate / Next / Cancel buttons are always visible.
    /// </summary>
    public class MainDialog : Form
    {
        // Normal Column row indices
        private const int RowHeightH = 0;
        private const int RowWidthB = 1;
        private const int RowCornerBarDia = 2;
        private const int RowMiddleBarsCountX = 3;
        private const int RowMiddleBarDiaX = 4;
        private const int RowMiddleBarsCountY = 5;
        private const int RowMiddleBarDiaY = 6;
        private const int RowCover = 7;

        // Links Add row indices
        private const int RowLinkDia = 0;
        private const int RowMainBarDia = 1;

        private TabControl tabControlMain;
        private TabPage tabNormalColumn;
        private TabPage tabLinksAdd;

        // Tab 1 controls
        private DataGridView dgvNormalColumn;
        private Button btnGenerateColumn;
        private Button btnCancelColumn;

        // Tab 2 controls
        private DataGridView dgvLinksAdd;
        private Label lblOffsetPreview;
        private Button btnNextLinks;
        private Button btnCancelLinks;

        public ColumnParameters ColumnParameters { get; private set; }
        public LinkParameters LinkParameters { get; private set; }
        public MainDialogAction SelectedAction { get; private set; } = MainDialogAction.None;

        public MainDialog(ColumnParameters initialColumnParams = null, LinkParameters initialLinkParams = null)
        {
            ColumnParameters = initialColumnParams ?? new ColumnParameters();
            LinkParameters = initialLinkParams ?? new LinkParameters();

            InitializeComponent();
            PopulateNormalColumnTable();
            PopulateLinksAddTable();
            UpdateOffsetPreviewLabel();
        }

        private void InitializeComponent()
        {
            this.Text = "Generate Rectangular Column";
            this.ClientSize = new Size(760, 410);
            this.MinimumSize = new Size(720, 400);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding = new Padding(10);

            this.tabControlMain = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "tabControlMain"
            };

            this.tabNormalColumn = new TabPage("Normal Column")
            {
                Padding = new Padding(8),
                UseVisualStyleBackColor = true
            };

            this.tabLinksAdd = new TabPage("Links Add")
            {
                Padding = new Padding(8),
                UseVisualStyleBackColor = true
            };

            BuildNormalColumnTab();
            BuildLinksAddTab();

            this.tabControlMain.TabPages.Add(this.tabNormalColumn);
            this.tabControlMain.TabPages.Add(this.tabLinksAdd);
            this.tabControlMain.SelectedIndexChanged += (s, e) =>
            {
                this.AcceptButton = (this.tabControlMain.SelectedTab == this.tabLinksAdd)
                    ? this.btnNextLinks
                    : this.btnGenerateColumn;
            };

            this.Controls.Add(this.tabControlMain);
            this.AcceptButton = this.btnGenerateColumn;
        }

        private void BuildNormalColumnTab()
        {
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            this.dgvNormalColumn = CreateParameterGrid("dgvNormalColumn");

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            this.btnCancelColumn = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 32),
                Margin = new Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnCancelColumn.Click += (s, e) =>
            {
                SelectedAction = MainDialogAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            this.btnGenerateColumn = new Button
            {
                Text = "Generate",
                Size = new Size(100, 32),
                Margin = new Padding(0, 0, 4, 0),
                UseVisualStyleBackColor = true
            };
            this.btnGenerateColumn.Click += BtnGenerateColumn_Click;

            // In RightToLeft FlowLayoutPanel, first added control is rightmost (Cancel), second is to its left (Generate)
            pnlButtons.Controls.Add(this.btnCancelColumn);
            pnlButtons.Controls.Add(this.btnGenerateColumn);

            layout.Controls.Add(this.dgvNormalColumn, 0, 0);
            layout.Controls.Add(pnlButtons, 0, 1);

            this.tabNormalColumn.Controls.Add(layout);
        }

        private void BuildLinksAddTab()
        {
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

            this.dgvLinksAdd = CreateParameterGrid("dgvLinksAdd");
            this.dgvLinksAdd.CellValueChanged += (s, e) => UpdateOffsetPreviewLabel();
            this.dgvLinksAdd.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (this.dgvLinksAdd.IsCurrentCellDirty)
                {
                    this.dgvLinksAdd.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            TableLayoutPanel bottomBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottomBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            this.lblOffsetPreview = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(40, 70, 115),
                Font = new Font(this.Font, FontStyle.Regular),
                Text = "Calculated Centerline Offset = 15.0 mm | Fixed Hook Length = 50 mm"
            };

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            this.btnCancelLinks = new Button
            {
                Text = "Cancel",
                Size = new Size(100, 32),
                Margin = new Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnCancelLinks.Click += (s, e) =>
            {
                SelectedAction = MainDialogAction.None;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            this.btnNextLinks = new Button
            {
                Text = "Next >",
                Size = new Size(100, 32),
                Margin = new Padding(0, 0, 4, 0),
                UseVisualStyleBackColor = true
            };
            this.btnNextLinks.Click += BtnNextLinks_Click;

            pnlButtons.Controls.Add(this.btnCancelLinks);
            pnlButtons.Controls.Add(this.btnNextLinks);

            bottomBar.Controls.Add(this.lblOffsetPreview, 0, 0);
            bottomBar.Controls.Add(pnlButtons, 1, 0);

            layout.Controls.Add(this.dgvLinksAdd, 0, 0);
            layout.Controls.Add(bottomBar, 0, 1);

            this.tabLinksAdd.Controls.Add(layout);
        }

        private static DataGridView CreateParameterGrid(string name)
        {
            DataGridView dgv = new DataGridView
            {
                Name = name,
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                BackgroundColor = SystemColors.Window,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            var colParameter = new DataGridViewTextBoxColumn
            {
                HeaderText = "Parameter",
                Name = "colParameter",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 225,
                DefaultCellStyle = { BackColor = Color.FromArgb(245, 246, 248) }
            };

            var colDescription = new DataGridViewTextBoxColumn
            {
                HeaderText = "Description",
                Name = "colDescription",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = { BackColor = Color.FromArgb(245, 246, 248) }
            };

            var colValue = new DataGridViewTextBoxColumn
            {
                HeaderText = "Value",
                Name = "colValue",
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 100,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            };

            dgv.Columns.AddRange(new DataGridViewColumn[] { colParameter, colDescription, colValue });
            return dgv;
        }

        private void PopulateNormalColumnTable()
        {
            dgvNormalColumn.Rows.Clear();
            dgvNormalColumn.Rows.Add("h (mm)", "Overall column height", ColumnParameters.HeightH.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("b (mm)", "Overall column width", ColumnParameters.WidthB.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Diameter of corner bars (mm)", "Diameter of longitudinal corner reinforcement", ColumnParameters.CornerBarDiameter.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Number of middle bars about X-X (mm)", "Number of additional reinforcement bars along the horizontal direction", ColumnParameters.MiddleBarsCountX.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Dia of middle bars about X-X (mm)", "Diameter of horizontal middle reinforcement bars", ColumnParameters.MiddleBarDiameterX.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Number of middle bars about Y-Y (mm)", "Number of additional reinforcement bars along the vertical direction", ColumnParameters.MiddleBarsCountY.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Dia of middle bars about Y-Y (mm)", "Diameter of vertical middle reinforcement bars", ColumnParameters.MiddleBarDiameterY.ToString(CultureInfo.InvariantCulture));
            dgvNormalColumn.Rows.Add("Cover on main bars (mm)", "Concrete cover measured to the outer surface of the main reinforcement bars", ColumnParameters.UserInputCover.ToString(CultureInfo.InvariantCulture));
        }

        private void PopulateLinksAddTable()
        {
            dgvLinksAdd.Rows.Clear();
            dgvLinksAdd.Rows.Add(
                "Link Diameter (mm)",
                "Diameter of the reinforcement link",
                LinkParameters.LinkDiameter.ToString(CultureInfo.InvariantCulture));

            dgvLinksAdd.Rows.Add(
                "Main Bar Diameter (mm)",
                "Diameter of the longitudinal reinforcement bars",
                LinkParameters.MainBarDiameter.ToString(CultureInfo.InvariantCulture));
        }

        private void UpdateOffsetPreviewLabel()
        {
            if (dgvLinksAdd == null || dgvLinksAdd.Rows.Count < 2 || lblOffsetPreview == null)
                return;

            string rawLink = GetCellString(dgvLinksAdd, RowLinkDia);
            string rawMain = GetCellString(dgvLinksAdd, RowMainBarDia);

            if (double.TryParse(rawLink, NumberStyles.Float, CultureInfo.InvariantCulture, out double linkDia) &&
                double.TryParse(rawMain, NumberStyles.Float, CultureInfo.InvariantCulture, out double mainDia) &&
                linkDia > 0 && mainDia > 0)
            {
                double offset = (mainDia / 2.0) + (linkDia / 2.0);
                lblOffsetPreview.Text =
                    $"Offset Distance = ({mainDia}/2) + ({linkDia}/2) = {offset:0.##} mm | Hook Length = {LinkParameters.HookLengthMm} mm";
            }
            else
            {
                lblOffsetPreview.Text = "Enter valid positive numbers for Link Diameter and Main Bar Diameter.";
            }
        }

        private void BtnGenerateColumn_Click(object sender, EventArgs e)
        {
            dgvNormalColumn.EndEdit();

            var parseErrors = new List<string>();
            var candidate = new ColumnParameters();

            if (TryReadDoubleCell(dgvNormalColumn, RowHeightH, "h (mm)", out double h, parseErrors))
                candidate.HeightH = h;
            if (TryReadDoubleCell(dgvNormalColumn, RowWidthB, "b (mm)", out double b, parseErrors))
                candidate.WidthB = b;
            if (TryReadDoubleCell(dgvNormalColumn, RowCornerBarDia, "Diameter of corner bars (mm)", out double cornerDia, parseErrors))
                candidate.CornerBarDiameter = cornerDia;
            if (TryReadIntCell(dgvNormalColumn, RowMiddleBarsCountX, "Number of middle bars about X-X (mm)", out int countX, parseErrors))
                candidate.MiddleBarsCountX = countX;
            if (TryReadDoubleCell(dgvNormalColumn, RowMiddleBarDiaX, "Dia of middle bars about X-X (mm)", out double diaX, parseErrors))
                candidate.MiddleBarDiameterX = diaX;
            if (TryReadIntCell(dgvNormalColumn, RowMiddleBarsCountY, "Number of middle bars about Y-Y (mm)", out int countY, parseErrors))
                candidate.MiddleBarsCountY = countY;
            if (TryReadDoubleCell(dgvNormalColumn, RowMiddleBarDiaY, "Dia of middle bars about Y-Y (mm)", out double diaY, parseErrors))
                candidate.MiddleBarDiameterY = diaY;
            if (TryReadDoubleCell(dgvNormalColumn, RowCover, "Cover on main bars (mm)", out double cover, parseErrors))
                candidate.UserInputCover = cover;

            if (parseErrors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, parseErrors), "Invalid Input Format", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!candidate.Validate(out List<string> validationErrors))
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, validationErrors), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ColumnParameters = candidate;
            SelectedAction = MainDialogAction.GenerateNormalColumn;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnNextLinks_Click(object sender, EventArgs e)
        {
            dgvLinksAdd.EndEdit();

            var parseErrors = new List<string>();
            var candidate = new LinkParameters
            {
                SelectedLinkType = LinkParameters.SelectedLinkType
            };

            if (TryReadDoubleCell(dgvLinksAdd, RowLinkDia, "Link Diameter (mm)", out double linkDia, parseErrors))
                candidate.LinkDiameter = linkDia;
            if (TryReadDoubleCell(dgvLinksAdd, RowMainBarDia, "Main Bar Diameter (mm)", out double mainDia, parseErrors))
                candidate.MainBarDiameter = mainDia;

            if (parseErrors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, parseErrors), "Invalid Input Format", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!candidate.Validate(out List<string> validationErrors))
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, validationErrors), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LinkParameters = candidate;
            SelectedAction = MainDialogAction.ProceedToLinksAdd;
            DialogResult = DialogResult.OK;
            Close();
        }

        public void SelectLinksAddTab()
        {
            this.tabControlMain.SelectedTab = this.tabLinksAdd;
            this.AcceptButton = this.btnNextLinks;
        }

        private static bool TryReadDoubleCell(DataGridView dgv, int rowIndex, string paramName, out double value, List<string> errors)
        {
            value = 0.0;
            string raw = GetCellString(dgv, rowIndex);
            if (string.IsNullOrWhiteSpace(raw))
            {
                errors.Add($"'{paramName}' cannot be empty.");
                return false;
            }

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                errors.Add($"'{paramName}' must be a valid number (entered: '{raw}').");
                return false;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                errors.Add($"'{paramName}' must be a finite number.");
                return false;
            }

            return true;
        }

        private static bool TryReadIntCell(DataGridView dgv, int rowIndex, string paramName, out int value, List<string> errors)
        {
            value = 0;
            string raw = GetCellString(dgv, rowIndex);
            if (string.IsNullOrWhiteSpace(raw))
            {
                errors.Add($"'{paramName}' cannot be empty.");
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                !int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            {
                errors.Add($"'{paramName}' must be a whole integer number (entered: '{raw}').");
                return false;
            }

            return true;
        }

        private static string GetCellString(DataGridView dgv, int rowIndex)
        {
            object cellVal = dgv.Rows[rowIndex].Cells[2].Value;
            return cellVal?.ToString()?.Trim() ?? string.Empty;
        }
    }

    // --- From UI/LinksAddDialog.cs ---
    /// <summary>
    /// Standalone parameter input dialog for "Links Add" (Link Diameter and Main Bar Diameter).
    /// </summary>
    public class LinksAddDialog : Form
    {
        private const int RowLinkDia = 0;
        private const int RowMainBarDia = 1;

        private DataGridView dgvLinks;
        private Button btnNext;
        private Button btnCancel;

        public LinkParameters Parameters { get; private set; }

        public LinksAddDialog(LinkParameters initialParameters = null)
        {
            Parameters = initialParameters ?? new LinkParameters();
            InitializeComponent();
            PopulateTable();
        }

        private void InitializeComponent()
        {
            this.Text = "Links Add - Parameter Input";
            this.ClientSize = new Size(680, 230);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding = new Padding(12);

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

            this.dgvLinks = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                BackgroundColor = SystemColors.Window,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect
            };

            var colParameter = new DataGridViewTextBoxColumn
            {
                HeaderText = "Parameter",
                ReadOnly = true,
                Width = 210,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { BackColor = Color.FromArgb(245, 246, 248) }
            };

            var colDescription = new DataGridViewTextBoxColumn
            {
                HeaderText = "Description",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { BackColor = Color.FromArgb(245, 246, 248) }
            };

            var colValue = new DataGridViewTextBoxColumn
            {
                HeaderText = "Value",
                ReadOnly = false,
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            };

            this.dgvLinks.Columns.AddRange(new DataGridViewColumn[] { colParameter, colDescription, colValue });

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            this.btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(96, 32),
                Margin = new Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            this.btnNext = new Button
            {
                Text = "Next >",
                Size = new Size(96, 32),
                Margin = new Padding(0, 0, 4, 0),
                UseVisualStyleBackColor = true
            };
            this.btnNext.Click += BtnNext_Click;

            pnlButtons.Controls.Add(this.btnCancel);
            pnlButtons.Controls.Add(this.btnNext);

            layout.Controls.Add(this.dgvLinks, 0, 0);
            layout.Controls.Add(pnlButtons, 0, 1);

            this.Controls.Add(layout);
            this.AcceptButton = this.btnNext;
            this.CancelButton = this.btnCancel;
        }

        private void PopulateTable()
        {
            dgvLinks.Rows.Clear();
            dgvLinks.Rows.Add("Link Diameter (mm)", "Diameter of the reinforcement link", Parameters.LinkDiameter.ToString(CultureInfo.InvariantCulture));
            dgvLinks.Rows.Add("Main Bar Diameter (mm)", "Diameter of the longitudinal reinforcement bars", Parameters.MainBarDiameter.ToString(CultureInfo.InvariantCulture));
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            dgvLinks.EndEdit();

            var errors = new List<string>();
            var candidate = new LinkParameters { SelectedLinkType = Parameters.SelectedLinkType };

            string rawLink = dgvLinks.Rows[RowLinkDia].Cells[2].Value?.ToString()?.Trim() ?? string.Empty;
            string rawMain = dgvLinks.Rows[RowMainBarDia].Cells[2].Value?.ToString()?.Trim() ?? string.Empty;

            if (!double.TryParse(rawLink, NumberStyles.Float, CultureInfo.InvariantCulture, out double linkDia) &&
                !double.TryParse(rawLink, NumberStyles.Float, CultureInfo.CurrentCulture, out linkDia))
            {
                errors.Add("Link Diameter (mm) must be a valid number.");
            }
            else
            {
                candidate.LinkDiameter = linkDia;
            }

            if (!double.TryParse(rawMain, NumberStyles.Float, CultureInfo.InvariantCulture, out double mainDia) &&
                !double.TryParse(rawMain, NumberStyles.Float, CultureInfo.CurrentCulture, out mainDia))
            {
                errors.Add("Main Bar Diameter (mm) must be a valid number.");
            }
            else
            {
                candidate.MainBarDiameter = mainDia;
            }

            if (errors.Count == 0 && !candidate.Validate(out List<string> valErrors))
            {
                errors.AddRange(valErrors);
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Parameters = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // --- From UI/LinkTypeSelectionDialog.cs ---
    /// <summary>
    /// Simple, clean text-only dialog for selecting one of the three reinforcement link types:
    /// 1. 1 Leg Type
    /// 2. 2 Legs Type - Outer
    /// 3. 2 Legs Type - Inner
    /// All three RadioButtons share the same parent GroupBox so only one can be selected at a time,
    /// with no custom GDI+ image drawing to ensure maximum reliability.
    /// </summary>
    public class LinkTypeSelectionDialog : Form
    {
        private GroupBox grpLinkTypes;
        private RadioButton rbOneLeg;
        private RadioButton rbTwoLegsOuter;
        private RadioButton rbTwoLegsInner;
        private Button btnBack;
        private Button btnNext;
        private Button btnCancel;

        public LinkType SelectedLinkType { get; private set; }
        public bool NavigateBackRequested { get; private set; }

        private readonly LinkParameters _parameters;

        public LinkTypeSelectionDialog(LinkParameters parameters)
        {
            _parameters = parameters ?? new LinkParameters();
            SelectedLinkType = _parameters.SelectedLinkType;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Link Type";
            this.ClientSize = new Size(520, 270);
            this.MinimumSize = new Size(500, 260);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Padding = new Padding(14);

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

            this.grpLinkTypes = new GroupBox
            {
                Text = $"Select Reinforcement Link Type (Offset = {_parameters.OffsetDistance:0.##} mm, Hook = {LinkParameters.HookLengthMm} mm)",
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 12, 16, 12)
            };

            // All three RadioButtons are direct children of grpLinkTypes so Windows Forms
            // automatically unchecks the others when any option is clicked.
            this.rbOneLeg = new RadioButton
            {
                Text = "1. 1 Leg Type   (Click 2 main bar centers: 1st bar -> 2nd bar)",
                AutoSize = true,
                Location = new Point(20, 36),
                Font = new Font(this.Font.FontFamily, 9.5f, FontStyle.Regular),
                Checked = (SelectedLinkType == LinkType.OneLeg)
            };

            this.rbTwoLegsOuter = new RadioButton
            {
                Text = "2. 2 Legs Type – Outer   (Click 2 main bar centers: Top-Left -> Bottom-Right)",
                AutoSize = true,
                Location = new Point(20, 82),
                Font = new Font(this.Font.FontFamily, 9.5f, FontStyle.Regular),
                Checked = (SelectedLinkType == LinkType.TwoLegsOuter)
            };

            this.rbTwoLegsInner = new RadioButton
            {
                Text = "3. 2 Legs Type – Inner   (Click 4 main bar centers: 1st -> 2nd -> 3rd -> 4th)",
                AutoSize = true,
                Location = new Point(20, 128),
                Font = new Font(this.Font.FontFamily, 9.5f, FontStyle.Regular),
                Checked = (SelectedLinkType == LinkType.TwoLegsInner)
            };

            // Ensure at least one option is checked
            if (!this.rbOneLeg.Checked && !this.rbTwoLegsOuter.Checked && !this.rbTwoLegsInner.Checked)
            {
                this.rbOneLeg.Checked = true;
            }

            this.grpLinkTypes.Controls.Add(this.rbOneLeg);
            this.grpLinkTypes.Controls.Add(this.rbTwoLegsOuter);
            this.grpLinkTypes.Controls.Add(this.rbTwoLegsInner);

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };

            this.btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(92, 30),
                Margin = new Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            this.btnNext = new Button
            {
                Text = "Next >",
                Size = new Size(92, 30),
                Margin = new Padding(8, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnNext.Click += BtnNext_Click;

            this.btnBack = new Button
            {
                Text = "< Back",
                Size = new Size(92, 30),
                Margin = new Padding(0, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            this.btnBack.Click += (s, e) =>
            {
                NavigateBackRequested = true;
                DialogResult = DialogResult.Retry;
                Close();
            };

            pnlButtons.Controls.Add(this.btnCancel);
            pnlButtons.Controls.Add(this.btnNext);
            pnlButtons.Controls.Add(this.btnBack);

            mainLayout.Controls.Add(this.grpLinkTypes, 0, 0);
            mainLayout.Controls.Add(pnlButtons, 0, 1);

            this.Controls.Add(mainLayout);
            this.AcceptButton = this.btnNext;
            this.CancelButton = this.btnCancel;
        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            if (rbTwoLegsOuter.Checked)
            {
                SelectedLinkType = LinkType.TwoLegsOuter;
            }
            else if (rbTwoLegsInner.Checked)
            {
                SelectedLinkType = LinkType.TwoLegsInner;
            }
            else
            {
                SelectedLinkType = LinkType.OneLeg;
            }

            _parameters.SelectedLinkType = SelectedLinkType;
            NavigateBackRequested = false;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // --- From Cad/ZwCadColumnDrawer.cs ---
    /// <summary>
    /// Responsible for creating CAD database entities in Model Space inside a transaction.
    /// Supports both ZWCAD (#define ZWCAD) and AutoCAD 2021.
    /// </summary>
    public static class ZwCadColumnDrawer
    {
        public static void DrawColumnSection(Document doc, ColumnSectionGeometry geometry)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (geometry == null)
                throw new ArgumentNullException(nameof(geometry));

            Database db = doc.Database;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
                    blockTable[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                // 1. Draw Outer Concrete Rectangular Boundary (Closed Polyline)
                using (Polyline outerConcretePoly = CreateClosedPolyline(geometry.OuterConcreteVertices, db))
                {
                    modelSpace.AppendEntity(outerConcretePoly);
                    tr.AddNewlyCreatedDBObject(outerConcretePoly, true);
                }

                // 2. Draw Inner Stirrup / Tie Boundary (Closed Polyline with rounded 90-degree corners)
                using (Polyline innerStirrupPoly = CreateClosedPolyline(geometry.InnerStirrupVertices, db))
                {
                    modelSpace.AppendEntity(innerStirrupPoly);
                    tr.AddNewlyCreatedDBObject(innerStirrupPoly, true);
                }

                // 3. Draw Longitudinal Reinforcement Bars (Circles)
                foreach (ReinforcingBarGeometry bar in geometry.LongitudinalBars)
                {
                    Point3d center3d = new Point3d(bar.Center.X, bar.Center.Y, 0.0);
                    using (Circle barCircle = new Circle(center3d, Vector3d.ZAxis, bar.Radius))
                    {
                        barCircle.SetDatabaseDefaults(db);
                        modelSpace.AppendEntity(barCircle);
                        tr.AddNewlyCreatedDBObject(barCircle, true);
                    }
                }

                // 4. Draw Horizontal Dimension Line (Overall Width b, placed above column)
                using (RotatedDimension horizontalDim = CreateRotatedDimension(geometry.HorizontalWidthDimension, db))
                {
                    modelSpace.AppendEntity(horizontalDim);
                    tr.AddNewlyCreatedDBObject(horizontalDim, true);
                }

                // 5. Draw Vertical Dimension Line (Overall Height h, placed on left side of column)
                using (RotatedDimension verticalDim = CreateRotatedDimension(geometry.VerticalHeightDimension, db))
                {
                    modelSpace.AppendEntity(verticalDim);
                    tr.AddNewlyCreatedDBObject(verticalDim, true);
                }

                tr.Commit();
            }
        }

        private static Polyline CreateClosedPolyline(IReadOnlyList<PolylineVertex2D> vertices, Database db)
        {
            Polyline poly = new Polyline(vertices.Count);
            poly.SetDatabaseDefaults(db);

            for (int i = 0; i < vertices.Count; i++)
            {
                poly.AddVertexAt(i, new Point2d(vertices[i].X, vertices[i].Y), vertices[i].Bulge, 0.0, 0.0);
            }

            poly.Closed = true;
            return poly;
        }

        private static RotatedDimension CreateRotatedDimension(DimensionSpec spec, Database db)
        {
            Point3d ext1 = new Point3d(spec.ExtensionLine1Point.X, spec.ExtensionLine1Point.Y, 0.0);
            Point3d ext2 = new Point3d(spec.ExtensionLine2Point.X, spec.ExtensionLine2Point.Y, 0.0);
            Point3d dimLinePt = new Point3d(spec.DimensionLinePoint.X, spec.DimensionLinePoint.Y, 0.0);

            RotatedDimension dim = new RotatedDimension(
                spec.RotationRadians,
                ext1,
                ext2,
                dimLinePt,
                string.Empty,
                db.Dimstyle);

            dim.SetDatabaseDefaults(db);
            dim.DimensionStyle = db.Dimstyle;
            dim.Dimtxt = spec.TextHeight;
            dim.Dimtih = false;
            dim.Dimtoh = false;
            dim.Dimtad = 1;
            dim.Dimasz = 10.0;
            dim.Dimexe = 8.0;
            dim.Dimexo = 8.0;
            dim.Dimgap = 5.0;
            dim.Dimdec = 0;

            return dim;
        }
    }

    // --- From Cad/LinkDrawingService.cs ---
    /// <summary>
    /// Creates the calculated reinforcement link (stirrup / cross-tie) geometry in Model Space
    /// using a lightweight Polyline with curved arc bulges inside a database transaction.
    /// </summary>
    public static class LinkDrawingService
    {
        public static ObjectId DrawLink(Document doc, LinkPolylineGeometry linkGeometry)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (linkGeometry == null || linkGeometry.Vertices == null || linkGeometry.Vertices.Count < 2)
                throw new ArgumentException("Valid link polyline geometry with at least 2 vertices is required.", nameof(linkGeometry));

            Database db = doc.Database;
            ObjectId createdPolyId = ObjectId.Null;

            using (DocumentLock docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
                    blockTable[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                using (Polyline linkPoly = new Polyline(linkGeometry.Vertices.Count))
                {
                    linkPoly.SetDatabaseDefaults(db);

                    for (int i = 0; i < linkGeometry.Vertices.Count; i++)
                    {
                        PolylineVertex2D v = linkGeometry.Vertices[i];
                        linkPoly.AddVertexAt(i, new Point2d(v.X, v.Y), v.Bulge, 0.0, 0.0);
                    }

                    linkPoly.Closed = linkGeometry.IsClosed;

                    createdPolyId = modelSpace.AppendEntity(linkPoly);
                    tr.AddNewlyCreatedDBObject(linkPoly, true);
                }

                tr.Commit();
            }

            return createdPolyId;
        }
    }

    // --- From Commands/LinkCommandHandler.cs ---
    /// <summary>
    /// Handles interactive CAD point selection for the "Links Add" feature:
    /// - Prompts the user with clear command-line messages for each LinkType.
    /// - Draws the link immediately once the required bar centers (2 or 4 points) are clicked.
    /// - Allows consecutive link placement simply by clicking the next set of bar centers,
    ///   or pressing Enter / Space / ESC to finish.
    /// </summary>
    public static class LinkCommandHandler
    {
        public static void ExecuteLinkPlacementWorkflow(Document doc, LinkParameters parameters)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            Editor ed = doc.Editor;

            ed.WriteMessage(
                $"\n[Links Add] {GetLinkTypeDisplayName(parameters.SelectedLinkType)} | " +
                $"Offset = {parameters.OffsetDistance:0.##} mm | Hook = {LinkParameters.HookLengthMm} mm.\n");

            while (true)
            {
                if (!TryPromptBarCenters(ed, parameters.SelectedLinkType, out List<Point2D> selectedCenters))
                {
                    ed.WriteMessage("\n*Links Add finished.*\n");
                    break;
                }

                try
                {
                    LinkPolylineGeometry linkGeometry = LinkGeometryCalculator.Calculate(parameters, selectedCenters);
                    LinkDrawingService.DrawLink(doc, linkGeometry);

                    ed.WriteMessage(
                        $"\n[Links Add] {GetLinkTypeDisplayName(parameters.SelectedLinkType)} created. " +
                        "(Select next bar centers to continue, or press Enter/ESC to finish.)\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[Links Add Error] {ex.Message}\n");
                    MessageBox.Show(
                        ex.Message,
                        "Link Geometry Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private static bool TryPromptBarCenters(Editor ed, LinkType linkType, out List<Point2D> wcsCenters)
        {
            wcsCenters = new List<Point2D>();
            string[] prompts = GetPromptsForLinkType(linkType);

            Point3d? lastUcsPoint = null;

            for (int i = 0; i < prompts.Length; i++)
            {
                PromptPointOptions ptOpts = new PromptPointOptions(prompts[i])
                {
                    // Allow pressing Enter/Space on the first prompt to exit cleanly after placing links
                    AllowNone = (i == 0)
                };

                if (lastUcsPoint.HasValue)
                {
                    ptOpts.UseBasePoint = true;
                    ptOpts.BasePoint = lastUcsPoint.Value;
                    ptOpts.UseDashedLine = true;
                }

                PromptPointResult ptRes = ed.GetPoint(ptOpts);
                if (ptRes.Status != PromptStatus.OK)
                {
                    return false;
                }

                lastUcsPoint = ptRes.Value;
                Point3d wcsPt = ptRes.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                wcsCenters.Add(new Point2D(wcsPt.X, wcsPt.Y));
            }

            return true;
        }

        private static string[] GetPromptsForLinkType(LinkType linkType)
        {
            switch (linkType)
            {
                case LinkType.OneLeg:
                    return new[]
                    {
                        "\nSelect first main bar center: ",
                        "\nSelect second main bar center: "
                    };

                case LinkType.TwoLegsOuter:
                    return new[]
                    {
                        "\nSelect top-left main bar center: ",
                        "\nSelect bottom-right main bar center: "
                    };

                case LinkType.TwoLegsInner:
                    return new[]
                    {
                        "\nSelect first main bar center: ",
                        "\nSelect second main bar center: ",
                        "\nSelect third main bar center: ",
                        "\nSelect fourth main bar center: "
                    };

                default:
                    throw new NotSupportedException($"Unsupported link type: {linkType}");
            }
        }

        public static string GetLinkTypeDisplayName(LinkType linkType)
        {
            switch (linkType)
            {
                case LinkType.OneLeg:
                    return "1 Leg Type";
                case LinkType.TwoLegsOuter:
                    return "2 Legs Type – Outer";
                case LinkType.TwoLegsInner:
                    return "2 Legs Type – Inner";
                default:
                    return linkType.ToString();
            }
        }
    }
}