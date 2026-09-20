using PilotsDeck.Actions;
using PilotsDeck.Actions.Advanced;
using PilotsDeck.Actions.Advanced.SettingsModel;
using PilotsDeck.Plugin;
using PilotsDeck.Plugin.Render;
using PilotsDeck.Simulator;
using PilotsDeck.UI.ActionDesignerUI;
using PilotsDeck.UI.ActionDesignerUI.TreeViews;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PilotsDeck.UI.ActionDesignerUI.ViewModels
{
    public enum ActionTemplate
    {
        NONE = 0,
        DISPLAY,
        SWITCH,
        DYNAMIC,
        KORRY,
        RADIO,
        GAUGE
    }

    public static class ViewModelHelper
    {
        public static string T(string text) => DesignerLocalization.Translate(text);

        private static Dictionary<DISPLAY_ELEMENT, string> ElementTypesSource { get; } = new()
        {
            {DISPLAY_ELEMENT.IMAGE, "Image" },
            {DISPLAY_ELEMENT.VALUE, "Value" },
            {DISPLAY_ELEMENT.TEXT, "Text" },
            {DISPLAY_ELEMENT.GAUGE, "Gauge" },
            {DISPLAY_ELEMENT.PRIMITIVE, "Primitive" },
        };
        public static Dictionary<DISPLAY_ELEMENT, string> ElementTypes => DesignerLocalization.TranslateDictionary(ElementTypesSource);

        public static void SetElementTypes(Collection<KeyValuePair<Enum, string>> target)
        {
            foreach (var type in ElementTypes)
                target.Add(new(type.Key, type.Value));
        }

        public static void SetManipulatorTypes(Collection<KeyValuePair<Enum, string>> target, DISPLAY_ELEMENT type)
        {
            target.Add(new(ELEMENT_MANIPULATOR.VISIBLE, T("Visible")));
            if (type == DISPLAY_ELEMENT.VALUE)
                target.Add(new(ELEMENT_MANIPULATOR.FORMAT, T("Format")));
            if (type == DISPLAY_ELEMENT.GAUGE)
                target.Add(new(ELEMENT_MANIPULATOR.INDICATOR, T("Indicator")));
            target.Add(new(ELEMENT_MANIPULATOR.COLOR, T("Color")));
            target.Add(new(ELEMENT_MANIPULATOR.SIZEPOS, T("Size / Position")));
            target.Add(new(ELEMENT_MANIPULATOR.ROTATE, T("Rotate")));
            target.Add(new(ELEMENT_MANIPULATOR.TRANSPARENCY, T("Transparency")));
            target.Add(new(ELEMENT_MANIPULATOR.FLASH, T("Flash")));
        }

        public static Dictionary<ELEMENT_MANIPULATOR, string> GetManipulatorTypes(TreeItemData item)
        {
            Dictionary<ELEMENT_MANIPULATOR, string> dict = [];

            dict.Add(ELEMENT_MANIPULATOR.VISIBLE, T("Visible"));
            if (item?.ElementType == DISPLAY_ELEMENT.VALUE || item == null)
                dict.Add(ELEMENT_MANIPULATOR.FORMAT, T("Format"));
            if (item?.ElementType == DISPLAY_ELEMENT.GAUGE || item == null)
                dict.Add(ELEMENT_MANIPULATOR.INDICATOR, T("Indicator"));
            dict.Add(ELEMENT_MANIPULATOR.COLOR, T("Color"));
            dict.Add(ELEMENT_MANIPULATOR.SIZEPOS, T("Size / Position"));
            dict.Add(ELEMENT_MANIPULATOR.ROTATE, T("Rotate"));
            dict.Add(ELEMENT_MANIPULATOR.TRANSPARENCY, T("Transparency"));
            dict.Add(ELEMENT_MANIPULATOR.FLASH, T("Flash"));

            return dict;
        }

        private static Dictionary<ELEMENT_MANIPULATOR, string> ManipulatorTypesSource { get; } = new()
        {
            { ELEMENT_MANIPULATOR.VISIBLE, "Visible" },
            { ELEMENT_MANIPULATOR.FORMAT, "Format" },
            { ELEMENT_MANIPULATOR.INDICATOR, "Indicator" },
            { ELEMENT_MANIPULATOR.COLOR, "Color" },
            { ELEMENT_MANIPULATOR.SIZEPOS, "Size / Position" },
            { ELEMENT_MANIPULATOR.ROTATE, "Rotate" },
            { ELEMENT_MANIPULATOR.TRANSPARENCY, "Transparency" },
            { ELEMENT_MANIPULATOR.FLASH, "Flash" },
        };
        public static Dictionary<ELEMENT_MANIPULATOR, string> ManipulatorTypes => DesignerLocalization.TranslateDictionary(ManipulatorTypesSource);

        private static Dictionary<PrimitiveType, string> PrimitiveTypesSource { get; } = new()
        {
            { PrimitiveType.LINE, "Line" },
            { PrimitiveType.RECTANGLE, "Rectangle" },
            { PrimitiveType.RECTANGLE_FILLED, "Rectangle Filled" },
            { PrimitiveType.CIRCLE, "Ellipse" },
            { PrimitiveType.CIRCLE_FILLED, "Ellipse Filled" },
        };
        public static Dictionary<PrimitiveType, string> PrimitiveTypes => DesignerLocalization.TranslateDictionary(PrimitiveTypesSource);


        private static Dictionary<ActionTemplate, string> ActionTemplatesSource { get; } = new()
        {
            { ActionTemplate.NONE, "No Template" },
            { ActionTemplate.DISPLAY, "Display Value" },
            { ActionTemplate.SWITCH, "Simple Button" },
            { ActionTemplate.DYNAMIC, "Dynamic Button" },
            { ActionTemplate.KORRY, "Korry Button" },
            { ActionTemplate.RADIO, "COM Radio" },
            { ActionTemplate.GAUGE, "Display Gauge" },
        };
        public static Dictionary<ActionTemplate, string> ActionTemplates => DesignerLocalization.TranslateDictionary(ActionTemplatesSource);

        public static void SetTemplateTypes(Collection<KeyValuePair<Enum, string>> target)
        {
            foreach (var type in ActionTemplates)
                target.Add(new(type.Key, type.Value));
        }

        private static Dictionary<IndicatorType, string> IndicatorTypesSource { get; } = new()
        {
            { IndicatorType.TRIANGLE, "Triangle" },
            { IndicatorType.CIRCLE, "Circle" },
            { IndicatorType.DOT, "Dot" },
            { IndicatorType.LINE, "Line" },
            { IndicatorType.IMAGE, "Image" },
        };
        public static Dictionary<IndicatorType, string> IndicatorTypes => DesignerLocalization.TranslateDictionary(IndicatorTypesSource);

        private static Dictionary<CenterType, string> CenterTypesSource { get; } = new()
        {
            { CenterType.NONE, "No Centering" },
            { CenterType.HORIZONTAL, "Horizontal" },
            { CenterType.VERTICAL, "Vertical" },
            { CenterType.BOTH, "Both" },
        };
        public static Dictionary<CenterType, string> CenterTypes => DesignerLocalization.TranslateDictionary(CenterTypesSource);

        private static Dictionary<ScaleType, string> ScaleTypesSource { get; } = new()
        {
            { ScaleType.NONE, "No Scaling" },
            { ScaleType.DEFAULT_KEEP, "Scale to Default Raster" },
            { ScaleType.DEFAULT_STRETCH, "Stretch Default Raster" },
            { ScaleType.DEVICE_KEEP, "Scale to Device Raster" },
            { ScaleType.DEVICE_STRETCH, "Stretch to Device Raster" },
        };
        public static Dictionary<ScaleType, string> ScaleTypes => DesignerLocalization.TranslateDictionary(ScaleTypesSource);

        public static Dictionary<SimCommandType, string> GetSimTypes()
        {
            Dictionary<SimCommandType, string> dict = [];
            var model = new PropertyInspectorModel().ActionTypes;
            foreach (var item in model)
                dict.Add(item.Value, T(item.Key));
            return dict;
        }

        private static Dictionary<StreamDeckCommand, string> DeckCommandTypesSource { get; } = new()
        {
            { StreamDeckCommand.KEY_DOWN, "Key Down" },
            { StreamDeckCommand.KEY_UP, "Key Up" },
            { StreamDeckCommand.DIAL_DOWN, "Dial Down" },
            { StreamDeckCommand.DIAL_UP, "Dial Up" },
            { StreamDeckCommand.DIAL_LEFT, "Dial Left" },
            { StreamDeckCommand.DIAL_RIGHT, "Dial Right" },
            { StreamDeckCommand.DIAL_LEFT_PRESSED, "Dial Left Pressed" },
            { StreamDeckCommand.DIAL_RIGHT_PRESSED, "Dial Right Pressed" },
            { StreamDeckCommand.TOUCH_TAP, "Touch Tap" },
        };
        public static Dictionary<StreamDeckCommand, string> DeckCommandTypes => DesignerLocalization.TranslateDictionary(DeckCommandTypesSource);

        private static Dictionary<Comparison, string> ComparisonTypesSource { get; } = new()
        {
            { Comparison.LESS, "<" },
            { Comparison.LESS_EQUAL, "<=" },
            { Comparison.GREATER, ">" },
            { Comparison.GREATER_EQUAL, ">=" },
            { Comparison.EQUAL, "==" },
            { Comparison.NOT_EQUAL, "!=" },
            { Comparison.CONTAINS, "contains" },
            { Comparison.NOT_CONTAINS, "not contains" },
            { Comparison.HAS_CHANGED, "has changed" },
        };
        public static Dictionary<Comparison, string> ComparisonTypes => DesignerLocalization.TranslateDictionary(ComparisonTypesSource);
    }
}
