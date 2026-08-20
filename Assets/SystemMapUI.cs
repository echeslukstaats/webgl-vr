using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SystemMapUI : MonoBehaviour {

    // ---- Palette -----------------------------------------------------------
    private static readonly Color PanelBg       = new Color(0.04f, 0.05f, 0.09f, 0.93f);
    private static readonly Color PanelEdge     = new Color(0.9f, 0.75f, 0.3f, 0.35f);
    private static readonly Color OverlayScrim  = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color GoldText      = new Color(0.95f, 0.80f, 0.35f, 1f);
    private static readonly Color GoldHighlight = new Color(1f, 0.88f, 0.50f, 1f);
    private static readonly Color BodyText      = new Color(0.82f, 0.80f, 0.72f, 1f);
    private static readonly Color DimText       = new Color(0.70f, 0.66f, 0.54f, 1f);
    private static readonly Color LabelText     = new Color(0.94f, 0.94f, 0.92f, 1f);
    private static readonly Color ButtonNormal  = new Color(0.12f, 0.12f, 0.16f, 0.95f);
    private static readonly Color ToggleOn      = new Color(0.80f, 0.65f, 0.20f, 1f);
    private static readonly Color ToggleOff     = new Color(0.20f, 0.20f, 0.24f, 1f);
    private static readonly Color SliderFill    = new Color(0.80f, 0.65f, 0.20f, 1f);
    private static readonly Color SliderBg      = new Color(0.16f, 0.16f, 0.20f, 1f);
    private const string WarnHex = "#FF5555";

    // ---- Type scale --------------------------------------------------------
    // The canvas is locked to 720 reference units tall, so these are real
    // pixels at a 720p viewport and scale proportionally beyond it.
    private const float FontPanelHeader = 21f;
    private const float FontSection     = 17f;
    private const float FontBody        = 16f;
    private const float FontSmall       = 14f;
    private const float FontValue       = 19f;
    private const float FontButton      = 12f;
    private const float FontPlanetLabel = 17f;
    private const float FontPlaceholder = 30f;

    // ---- Layout metrics ----------------------------------------------------
    private const float SidebarWidth   = 300f;
    private const float RightColWidth  = 420f;
    private const float Margin         = 16f;
    private const float StatRowHeight  = 21f;
    private const float StatLabelWidth = 168f;
    private const float LeaderLength   = 26f;

    /// Horizontal band the sidebar occupies, in canvas units. The map image is
    /// fitted to the right of this so the planets are never hidden behind it.
    public const float LeftReservedUnits = Margin + SidebarWidth + Margin;

    private Canvas canvas;
    private RectTransform canvasRT;

    private TMP_Text selectionNameText;
    private TMP_Text selectionTypeText;
    private TMP_Text warningText;
    private Dictionary<string, TMP_Text> statValues = new Dictionary<string, TMP_Text>();

    private Slider shieldingSlider;
    private TMP_Text shieldingValueText;

    private Slider missionSlider;
    private TMP_Text missionValueText;

    private Image[] hardwareButtonImages;
    private TMP_Text[] hardwareButtonTexts;

    private Image[] tierRowBgs;
    private TMP_Text[] tierRowBadges;
    private TMP_Text[] tierRowNames;

    private GameObject infoOverlay;
    private RectTransform reportPanel;
    private GameObject reportBody;
    private TMP_Text reportToggleText;
    private bool reportExpanded = true;
    private TMP_Text placeholderText;

    // The fidelity preview is always on - there is no toggle.
    private const bool PreviewActive = true;
    private Camera previewCamera;
    private RenderTexture previewRenderTex;
    private GameObject[] tierPreviewObjects;
    private RawImage previewDisplay;
    private TMP_Text previewLabel;


    void Start() {
        BuildUI();
        SetupPreviewScene();

        GameManager.Instance.OnLocationChanged += OnLocationChanged;
        GameManager.Instance.OnSubLocationChanged += OnSubLocationChanged;
        GameManager.Instance.OnFidelityChanged += OnFidelityChanged;

        GameManager.Instance.SetVRPreview(true);

        UpdateHardwareButtons(GameManager.Instance.HardwareClassIndex);

        if(GameManager.Instance.CurrentSelection.HasValue) {
            OnLocationChanged(GameManager.Instance.CurrentSelection.Value);
            ShowSubLocations(GameManager.Instance.CurrentSelection.Value);
        } else {
            ShowPreviewTier(-1);
        }
    }

    void LateUpdate() {
        if(canvasRT != null && (canvasRT.rect.width != lastCanvasW
            || canvasRT.rect.height != lastCanvasH)) {
            LayoutMapBackground();
        }

        UpdateSelectionIndicator();

        if(PreviewActive && tierPreviewObjects != null) {
            foreach(var obj in tierPreviewObjects) {
                if(obj != null && obj.activeSelf) {
                    obj.transform.Rotate(0, 45f * Time.deltaTime, 0, Space.Self);
                }
            }
        }
    }

    void OnDestroy() {
        if(GameManager.Instance != null) {
            GameManager.Instance.OnLocationChanged -= OnLocationChanged;
            GameManager.Instance.OnSubLocationChanged -= OnSubLocationChanged;
            GameManager.Instance.OnFidelityChanged -= OnFidelityChanged;
        }
        if(previewRenderTex != null) previewRenderTex.Release();
    }

    public void ExpandPlanet(SpaceEnvironment env) {
        ShowSubLocations(env);
    }

    private void BuildUI() {
        GameObject canvasGO = new GameObject("SystemMapCanvas");
        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        // Match height only: the canvas is then always exactly 720 units tall
        // whatever the aspect, which makes the vertical budgets deterministic.
        scaler.matchWidthOrHeight = 1f;

        canvasGO.AddComponent<GraphicRaycaster>();
        canvasRT = canvasGO.GetComponent<RectTransform>();

        BuildMapBackground(canvasRT);
        BuildSettingsSidebar(canvasRT);
        BuildReportPanel(canvasRT);
        BuildPlaceholderBlock(canvasRT);
        BuildInfoButton(canvasRT);
        BuildInfoOverlay(canvasRT);
        BuildBackButton(canvasRT);
    }

    // ---- Left sidebar ------------------------------------------------------
    // Vertical budget (696 available, 691 used): header 30 | sep 2 |
    // label 24 + note 30 + slider 34 + value 24, three times | sep 2 x3 |
    // fidelity 26 | preview 160   + 17 gaps x 7 + 32 padding

    private void BuildSettingsSidebar(RectTransform parent) {
        RectTransform sidebar = CreatePanel(parent, "SettingsSidebar", PanelBg, true);
        sidebar.anchorMin = new Vector2(0, 0);
        sidebar.anchorMax = new Vector2(0, 0);
        sidebar.pivot = new Vector2(0, 0);
        sidebar.anchoredPosition = new Vector2(Margin, 12);
        sidebar.sizeDelta = new Vector2(SidebarWidth, 696);

        VerticalLayoutGroup vlg = sidebar.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 16, 16);
        vlg.spacing = 7;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        TMP_Text header = CreateText(sidebar, "SidebarTitle", "PARAMETERS", FontPanelHeader, GoldText);
        header.alignment = TextAlignmentOptions.MidlineLeft;
        header.fontStyle = FontStyles.Bold;
        SetHeight(header, 30);

        CreateSeparator(sidebar);

        CreateSectionLabel(sidebar, "ShieldLabel", "SHIELDING LEVEL");
        CreateNote(sidebar, "ShieldNote", "Spacecraft hull thickness\nreducing radiation exposure");
        BuildShieldingSlider(sidebar);

        CreateSeparator(sidebar);

        CreateSectionLabel(sidebar, "MissionLabel", "MISSION DURATION");
        CreateNote(sidebar, "MissionNote", "Total years of operation\nat the selected location");
        BuildMissionSlider(sidebar);

        CreateSeparator(sidebar);

        CreateSectionLabel(sidebar, "HWLabel", "HARDWARE CLASS");
        CreateNote(sidebar, "HWNote", "Onboard compute hardware\nfor VR rendering");
        BuildHardwareSelector(sidebar);

        CreateSeparator(sidebar);

        previewLabel = CreateText(sidebar, "PreviewTierLabel", "FIDELITY: --", FontSection, GoldText);
        previewLabel.alignment = TextAlignmentOptions.Center;
        previewLabel.fontStyle = FontStyles.Bold;
        previewLabel.enableWordWrapping = false;
        SetHeight(previewLabel, 26);

        BuildPreviewDisplay(sidebar);
    }

    private TMP_Text CreateSectionLabel(RectTransform parent, string name, string label) {
        TMP_Text t = CreateText(parent, name, label, FontSection, DimText);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.fontStyle = FontStyles.Bold;
        t.enableWordWrapping = false;
        SetHeight(t, 24);
        return t;
    }

    private TMP_Text CreateNote(RectTransform parent, string name, string text) {
        TMP_Text t = CreateText(parent, name, text, FontSmall - 1f, DimText);
        t.alignment = TextAlignmentOptions.TopLeft;
        t.fontStyle = FontStyles.Italic;
        SetHeight(t, 30);
        return t;
    }

    private TMP_Text CreateValueReadout(RectTransform parent, string name, string text) {
        TMP_Text t = CreateText(parent, name, text, FontValue, GoldHighlight);
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.enableWordWrapping = false;
        SetHeight(t, 24);
        return t;
    }

    // ---- Sliders -----------------------------------------------------------

    private Slider BuildSlider(RectTransform parent, string name, float min, float max, float value) {
        GameObject sliderGO = new GameObject(name);
        sliderGO.transform.SetParent(parent, false);
        RectTransform sliderRT = sliderGO.AddComponent<RectTransform>();
        SetHeight(sliderGO.transform, 34);

        Slider slider = sliderGO.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;

        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderRT, false);
        RectTransform bgRT = bgGO.AddComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0, 0.34f);
        bgRT.anchorMax = new Vector2(1, 0.66f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = SliderBg;

        GameObject fillAreaGO = new GameObject("FillArea");
        fillAreaGO.transform.SetParent(sliderRT, false);
        RectTransform fillAreaRT = fillAreaGO.AddComponent<RectTransform>();
        fillAreaRT.anchorMin = new Vector2(0, 0.34f);
        fillAreaRT.anchorMax = new Vector2(1, 0.66f);
        fillAreaRT.offsetMin = Vector2.zero;
        fillAreaRT.offsetMax = Vector2.zero;

        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaRT, false);
        RectTransform fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
        fillGO.AddComponent<Image>().color = SliderFill;

        GameObject handleAreaGO = new GameObject("HandleSlideArea");
        handleAreaGO.transform.SetParent(sliderRT, false);
        RectTransform handleAreaRT = handleAreaGO.AddComponent<RectTransform>();
        handleAreaRT.anchorMin = Vector2.zero;
        handleAreaRT.anchorMax = Vector2.one;
        handleAreaRT.offsetMin = Vector2.zero;
        handleAreaRT.offsetMax = Vector2.zero;

        GameObject handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleAreaRT, false);
        RectTransform handleRT = handleGO.AddComponent<RectTransform>();
        handleRT.sizeDelta = new Vector2(28, 28);
        Image handleImg = handleGO.AddComponent<Image>();
        handleImg.color = GoldHighlight;

        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;
        slider.value = value;

        return slider;
    }

    private void BuildShieldingSlider(RectTransform parent) {
        int levels = RadiationCalculator.ShieldingCount;
        if(levels <= 0) levels = 3;

        shieldingSlider = BuildSlider(parent, "ShieldingSlider", 0, levels - 1,
            GameManager.Instance.ShieldingLevel);

        shieldingValueText = CreateValueReadout(parent, "ShieldValue",
            RadiationCalculator.GetShieldingLabel(GameManager.Instance.ShieldingLevel));

        shieldingSlider.onValueChanged.AddListener((val) => {
            int level = Mathf.RoundToInt(val);
            shieldingValueText.text = RadiationCalculator.GetShieldingLabel(level);
            GameManager.Instance.SetShieldingLevel(level);
        });
    }

    private void BuildMissionSlider(RectTransform parent) {
        missionSlider = BuildSlider(parent, "MissionSlider", 1, 20,
            GameManager.Instance.MissionDuration);

        missionValueText = CreateValueReadout(parent, "MissionValue",
            GameManager.Instance.MissionDuration + " YR");

        missionSlider.onValueChanged.AddListener((val) => {
            int years = Mathf.RoundToInt(val);
            missionValueText.text = years + " YR";
            GameManager.Instance.SetMissionDuration(years);
        });
    }

    // ---- Hardware class: four across, as in the mockup ---------------------

    private void BuildHardwareSelector(RectTransform parent) {
        int count = RadiationCalculator.HardwareNames.Length;

        GameObject rowGO = new GameObject("HardwareRow");
        rowGO.transform.SetParent(parent, false);
        rowGO.AddComponent<RectTransform>();
        SetHeight(rowGO.transform, 38);

        HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        hardwareButtonImages = new Image[count];
        hardwareButtonTexts = new TMP_Text[count];

        RectTransform rowRT = rowGO.GetComponent<RectTransform>();

        for(int i = 0; i < count; i++) {
            int index = i;
            GameObject btnGO = CreateChoiceButton(rowRT, "HW_" + i,
                RadiationCalculator.HardwareNames[i].ToUpper(), () => {
                    GameManager.Instance.SetHardwareClass(index);
                    UpdateHardwareButtons(index);
                });

            hardwareButtonImages[i] = btnGO.GetComponent<Image>();
            hardwareButtonTexts[i] = btnGO.GetComponentInChildren<TMP_Text>();
        }
    }

    private void UpdateHardwareButtons(int selectedIndex) {
        if(hardwareButtonImages == null) return;

        for(int i = 0; i < hardwareButtonImages.Length; i++) {
            bool on = (i == selectedIndex);
            hardwareButtonImages[i].color = on ? ToggleOn : ToggleOff;
            hardwareButtonTexts[i].color = on ? Color.black : DimText;
        }
    }

    private GameObject CreateChoiceButton(RectTransform parent, string name, string label, Action onClick) {
        GameObject btnGO = new GameObject(name);
        btnGO.transform.SetParent(parent, false);
        btnGO.AddComponent<RectTransform>();

        Image img = btnGO.AddComponent<Image>();
        img.color = ToggleOff;

        Button btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = cb;

        // Narrow buttons, so "LEGACY RH" is allowed to wrap onto two lines.
        TMP_Text txt = CreateText(btnGO.GetComponent<RectTransform>(), "Label", label, FontButton, DimText);
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = FontStyles.Bold;
        txt.enableWordWrapping = true;
        Stretch(txt.rectTransform);

        btn.onClick.AddListener(() => onClick());
        return btnGO;
    }

    private void BuildPreviewDisplay(RectTransform parent) {
        GameObject holder = new GameObject("PreviewHolder");
        holder.transform.SetParent(parent, false);
        holder.AddComponent<RectTransform>();
        SetHeight(holder.transform, 160);

        GameObject displayGO = new GameObject("PreviewImage");
        displayGO.transform.SetParent(holder.transform, false);
        RectTransform dRT = displayGO.AddComponent<RectTransform>();
        dRT.anchorMin = new Vector2(0.5f, 0.5f);
        dRT.anchorMax = new Vector2(0.5f, 0.5f);
        dRT.pivot = new Vector2(0.5f, 0.5f);
        dRT.anchoredPosition = Vector2.zero;
        dRT.sizeDelta = new Vector2(256, 160);
        previewDisplay = displayGO.AddComponent<RawImage>();
        previewDisplay.color = Color.white;

        Outline outline = displayGO.AddComponent<Outline>();
        outline.effectColor = new Color(GoldText.r, GoldText.g, GoldText.b, 0.4f);
        outline.effectDistance = new Vector2(2, 2);
    }

    // ---- Bottom right: mission report --------------------------------------
    // Collapses to just its title bar, so it can be cleared off the inner
    // planets and the Sun, which sit behind it on the right of the map.
    // Expanded: 32 padding + 30 header + 3 gap + 314 body = 379 of 384.

    private const float ReportExpandedHeight  = 384f;
    private const float ReportCollapsedHeight = 62f;

    private void BuildReportPanel(RectTransform parent) {
        reportPanel = CreatePanel(parent, "MissionReport", PanelBg, true);
        reportPanel.anchorMin = new Vector2(1, 0);
        reportPanel.anchorMax = new Vector2(1, 0);
        reportPanel.pivot = new Vector2(1, 0);
        reportPanel.anchoredPosition = new Vector2(-Margin, Margin);
        reportPanel.sizeDelta = new Vector2(RightColWidth, ReportExpandedHeight);

        VerticalLayoutGroup vlg = reportPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 16, 16);
        vlg.spacing = 3;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Title bar: stays visible when collapsed.
        GameObject headerRow = new GameObject("HeaderRow");
        headerRow.transform.SetParent(reportPanel, false);
        headerRow.AddComponent<RectTransform>();
        SetHeight(headerRow.transform, 30);

        HorizontalLayoutGroup hh = headerRow.AddComponent<HorizontalLayoutGroup>();
        hh.spacing = 8;
        hh.childAlignment = TextAnchor.MiddleLeft;
        hh.childForceExpandWidth = false;
        hh.childForceExpandHeight = true;
        hh.childControlWidth = true;
        hh.childControlHeight = true;

        RectTransform headerRT = headerRow.GetComponent<RectTransform>();

        selectionNameText = CreateText(headerRT, "SelectionName", "NO SELECTION",
            FontPanelHeader, GoldHighlight);
        selectionNameText.alignment = TextAlignmentOptions.MidlineLeft;
        selectionNameText.fontStyle = FontStyles.Bold;
        selectionNameText.enableWordWrapping = false;
        selectionNameText.GetComponent<LayoutElement>().flexibleWidth = 1f;

        GameObject toggleGO = CreateChoiceButton(headerRT, "ReportToggle", "▾",
            () => SetReportExpanded(!reportExpanded));

        LayoutElement tle = toggleGO.GetComponent<LayoutElement>();
        if(tle == null) tle = toggleGO.AddComponent<LayoutElement>();
        tle.preferredWidth = 34;
        tle.minWidth = 34;
        tle.flexibleWidth = 0f;

        toggleGO.GetComponent<Image>().color = ButtonNormal;
        reportToggleText = toggleGO.GetComponentInChildren<TMP_Text>();
        reportToggleText.color = GoldText;
        reportToggleText.fontSize = 18f;

        // Everything below the title bar, hidden as one unit when collapsed.
        GameObject body = new GameObject("ReportBody");
        body.transform.SetParent(reportPanel, false);
        body.AddComponent<RectTransform>();
        SetHeight(body.transform, 314);
        reportBody = body;

        VerticalLayoutGroup bv = body.AddComponent<VerticalLayoutGroup>();
        bv.spacing = 3;
        bv.childForceExpandWidth = true;
        bv.childForceExpandHeight = false;
        bv.childControlWidth = true;
        bv.childControlHeight = true;

        RectTransform bodyRT = body.GetComponent<RectTransform>();

        selectionTypeText = CreateText(bodyRT, "SelectionType",
            "Click a planet or moon to run the model.", FontSmall, DimText);
        selectionTypeText.alignment = TextAlignmentOptions.MidlineLeft;
        SetHeight(selectionTypeText, 20);

        CreateSeparator(bodyRT);

        CreateStatRow(bodyRT, "env",     "Environment");
        CreateStatRow(bodyRT, "source",  "Dominant Source");
        CreateStatRow(bodyRT, "conf",    "Data Confidence");
        CreateStatRow(bodyRT, "basetid", "Baseline TID");
        CreateStatRow(bodyRT, "shield",  "Shielding");
        CreateStatRow(bodyRT, "efftid",  "Effective TID");
        CreateStatRow(bodyRT, "dose",    "Mission Dose");
        CreateStatRow(bodyRT, "hw",      "Hardware");
        CreateStatRow(bodyRT, "tol",     "TID Tolerance");
        CreateStatRow(bodyRT, "life",    "Hardware Lifespan");
        CreateStatRow(bodyRT, "tier",    "Fidelity Tier");

        warningText = CreateText(bodyRT, "Warning", "", FontSmall, BodyText);
        warningText.alignment = TextAlignmentOptions.MidlineLeft;
        warningText.fontStyle = FontStyles.Bold;
        warningText.enableWordWrapping = false;
        SetHeight(warningText, 22);

        SetReportExpanded(true);
    }

    private void SetReportExpanded(bool expanded) {
        reportExpanded = expanded;

        if(reportBody != null) reportBody.SetActive(expanded);

        if(reportPanel != null) {
            reportPanel.sizeDelta = new Vector2(RightColWidth,
                expanded ? ReportExpandedHeight : ReportCollapsedHeight);
        }

        // Panel grows upward from the bottom edge, so down-chevron = collapse.
        if(reportToggleText != null) reportToggleText.text = expanded ? "▾" : "▴";
    }

    private void CreateStatRow(RectTransform parent, string key, string label) {
        GameObject row = new GameObject("Stat_" + key);
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>();
        SetHeight(row.transform, StatRowHeight);

        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;

        RectTransform rowRT = row.GetComponent<RectTransform>();

        TMP_Text lab = CreateText(rowRT, "Label", label, FontBody, DimText);
        lab.alignment = TextAlignmentOptions.MidlineLeft;
        lab.enableWordWrapping = false;
        LayoutElement lle = lab.GetComponent<LayoutElement>();
        lle.preferredWidth = StatLabelWidth;
        lle.flexibleWidth = 0f;

        TMP_Text val = CreateText(rowRT, "Value", "--", FontBody, BodyText);
        val.alignment = TextAlignmentOptions.MidlineRight;
        val.enableWordWrapping = false;
        val.GetComponent<LayoutElement>().flexibleWidth = 1f;

        statValues[key] = val;
    }

    private void SetStat(string key, string value) {
        TMP_Text t;
        if(statValues.TryGetValue(key, out t)) t.text = value;
    }

    private void ResetStats() {
        foreach(var kvp in statValues) kvp.Value.text = "--";
    }

    // ---- Centre-bottom placeholder ----------------------------------------
    // Styled and wired up, ready for copy to be dropped in.

    private void BuildPlaceholderBlock(RectTransform parent) {
        GameObject go = new GameObject("CentrePlaceholder");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.offsetMin = new Vector2(LeftReservedUnits + Margin, 120);
        rt.offsetMax = new Vector2(-(RightColWidth + Margin * 2f), 250);

        placeholderText = CreateText(rt, "PlaceholderText", "", FontPlaceholder, BodyText);
        placeholderText.alignment = TextAlignmentOptions.TopLeft;
        Stretch(placeholderText.rectTransform);

        // Starts empty and hidden; call SetPlaceholderText to supply copy.
        SetPlaceholderText(null);
    }

    /// Replace the centre-bottom copy. Pass null or empty to hide the block.
    public void SetPlaceholderText(string text) {
        if(placeholderText == null) return;
        placeholderText.text = text ?? "";
        placeholderText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    // ---- Info button and overlay ------------------------------------------

    private void BuildInfoButton(RectTransform parent) {
        GameObject btnGO = new GameObject("InfoButton");
        btnGO.transform.SetParent(parent, false);
        RectTransform rt = btnGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-24, -24);
        rt.sizeDelta = new Vector2(48, 48);

        Image img = btnGO.AddComponent<Image>();
        img.sprite = CircleSprite();
        img.color = Color.white;

        Button btn = btnGO.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        cb.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
        btn.colors = cb;

        TMP_Text glyph = CreateText(rt, "Glyph", "i", 28, new Color(0.05f, 0.05f, 0.08f));
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.fontStyle = FontStyles.Bold | FontStyles.Italic;
        Stretch(glyph.rectTransform);

        btn.onClick.AddListener(() => ToggleInfoOverlay(true));
    }

    private void ToggleInfoOverlay(bool show) {
        if(infoOverlay != null) infoOverlay.SetActive(show);
    }

    private void BuildInfoOverlay(RectTransform parent) {
        infoOverlay = new GameObject("InfoOverlay");
        infoOverlay.transform.SetParent(parent, false);
        RectTransform rt = infoOverlay.AddComponent<RectTransform>();
        Stretch(rt);

        // Scrim: opaque enough to dim the map and swallow stray clicks.
        Image scrim = infoOverlay.AddComponent<Image>();
        scrim.color = OverlayScrim;

        Button dismiss = infoOverlay.AddComponent<Button>();
        dismiss.targetGraphic = scrim;
        ColorBlock dcb = dismiss.colors;
        dcb.normalColor = Color.white;
        dcb.highlightedColor = Color.white;
        dcb.pressedColor = Color.white;
        dismiss.colors = dcb;
        dismiss.onClick.AddListener(() => ToggleInfoOverlay(false));

        RectTransform card = CreatePanel(rt, "InfoCard", new Color(0.05f, 0.06f, 0.10f, 0.99f), true);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(560, 560);

        VerticalLayoutGroup vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 8;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        TMP_Text header = CreateText(card, "InfoHeader", "ABOUT THIS MODEL", FontPanelHeader, GoldText);
        header.alignment = TextAlignmentOptions.MidlineLeft;
        header.fontStyle = FontStyles.Bold;
        SetHeight(header, 30);

        TMP_Text body = CreateText(card, "InfoBody",
            "VR Beyond Earth estimates the highest VR fidelity tier a spacecraft could "
            + "sustain at each location in the Solar System, from the radiation dose it "
            + "receives, the shielding around its electronics and the compute it carries.",
            FontBody, BodyText);
        body.alignment = TextAlignmentOptions.TopLeft;
        SetHeight(body, 90);

        CreateSeparator(card);

        TMP_Text legendHeader = CreateText(card, "LegendHeader", "VR FIDELITY TIERS", FontSection, GoldText);
        legendHeader.alignment = TextAlignmentOptions.MidlineLeft;
        legendHeader.fontStyle = FontStyles.Bold;
        SetHeight(legendHeader, 24);

        BuildTierLegendRows(card);

        CreateSeparator(card);

        TMP_Text sources = CreateText(card, "InfoSources",
            "Dose, hardware and tier figures are taken from Tables 3.1-3.3 of the source "
            + "study.  Built with Unity and WebGL.\ngithub.com/22328383/webgl-vr",
            FontSmall, DimText);
        sources.alignment = TextAlignmentOptions.TopLeft;
        SetHeight(sources, 60);

        GameObject closeGO = CreateChoiceButton(card, "CloseButton", "CLOSE",
            () => ToggleInfoOverlay(false));
        SetHeight(closeGO.transform, 42);
        closeGO.GetComponent<Image>().color = ButtonNormal;
        TMP_Text closeTxt = closeGO.GetComponentInChildren<TMP_Text>();
        closeTxt.color = GoldText;
        closeTxt.fontSize = FontSection;

        infoOverlay.SetActive(false);
    }

    private void BuildTierLegendRows(RectTransform parent) {
        int tiers = 4;
        tierRowBgs = new Image[tiers];
        tierRowBadges = new TMP_Text[tiers];
        tierRowNames = new TMP_Text[tiers];

        for(int t = 0; t < tiers; t++) {
            GameObject row = new GameObject("Tier_" + t);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>();
            SetHeight(row.transform, 32);

            Image rowBg = row.AddComponent<Image>();
            rowBg.color = ToggleOff;
            tierRowBgs[t] = rowBg;

            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(10, 10, 0, 0);
            h.spacing = 10;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;

            RectTransform rowRT = row.GetComponent<RectTransform>();

            TMP_Text badge = CreateText(rowRT, "Badge", RadiationCalculator.GetTierShortName(t), FontSmall, GoldText);
            badge.alignment = TextAlignmentOptions.MidlineLeft;
            badge.fontStyle = FontStyles.Bold;
            badge.enableWordWrapping = false;
            LayoutElement ble = badge.GetComponent<LayoutElement>();
            ble.preferredWidth = 56;
            ble.flexibleWidth = 0f;
            tierRowBadges[t] = badge;

            TMP_Text nameTxt = CreateText(rowRT, "Name",
                RadiationCalculator.GetTierName(t) + "   -   " + RadiationCalculator.GetTierReference(t),
                FontSmall, BodyText);
            nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
            nameTxt.enableWordWrapping = false;
            nameTxt.GetComponent<LayoutElement>().flexibleWidth = 1f;
            tierRowNames[t] = nameTxt;
        }

        HighlightTier(-1);
    }

    private void HighlightTier(int activeTier) {
        if(tierRowBgs == null) return;

        for(int t = 0; t < tierRowBgs.Length; t++) {
            bool on = (t == activeTier);
            tierRowBgs[t].color = on ? ToggleOn : ToggleOff;
            tierRowBadges[t].color = on ? Color.black : GoldText;
            tierRowNames[t].color = on ? Color.black : BodyText;
        }
    }

    private void BuildBackButton(RectTransform parent) {
        RectTransform btnRT = CreatePanel(parent, "BackButton", ButtonNormal, true);
        btnRT.anchorMin = new Vector2(1, 1);
        btnRT.anchorMax = new Vector2(1, 1);
        btnRT.pivot = new Vector2(1, 1);
        btnRT.anchoredPosition = new Vector2(-88, -24);
        btnRT.sizeDelta = new Vector2(120, 48);

        TMP_Text txt = CreateText(btnRT, "BackText", "BACK", FontValue, GoldText);
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.Center;
        Stretch(txt.rectTransform);

        Button btn = btnRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnRT.GetComponent<Image>();
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        btn.colors = cb;
    }

    // ---- 2D map --------------------------------------------------------
    // The map is a single background image with invisible circular hit
    // regions laid over each planet. Positions below were measured from
    // SolarMap.png (3868 x 2160) by connected-component analysis of the
    // artwork, and are normalised so they hold at any display size.

    private struct MapBody {
        public SpaceEnvironment env;
        public float ncx, ncy, nr;
        public MapBody(SpaceEnvironment e, float x, float y, float r) {
            env = e; ncx = x; ncy = y; nr = r;
        }
    }

    private static readonly MapBody[] MapBodies = {
        new MapBody(SpaceEnvironment.Neptune, 0.0738f, 0.5005f, 0.0224f),
        new MapBody(SpaceEnvironment.Uranus,  0.1587f, 0.4998f, 0.0238f),
        new MapBody(SpaceEnvironment.Saturn,  0.3285f, 0.4993f, 0.0491f),
        new MapBody(SpaceEnvironment.Jupiter, 0.5769f, 0.4984f, 0.0615f),
        new MapBody(SpaceEnvironment.Mars,    0.7214f, 0.4998f, 0.0034f),
        new MapBody(SpaceEnvironment.Earth,   0.7671f, 0.4995f, 0.0063f),
        new MapBody(SpaceEnvironment.Venus,   0.8184f, 0.4995f, 0.0058f),
        new MapBody(SpaceEnvironment.Mercury, 0.8581f, 0.4998f, 0.0026f),
    };

    private const float MapAspect  = 3868f / 2160f;
    // Mercury is only ~19px wide in the art, so small planets get a floor on
    // their target size. Venus and Mercury are the tightest pair at ~37 units
    // apart on a 720p canvas, so this must stay under that to avoid overlap.
    private const float MinHitSize = 34f;

    private class HitRegion {
        public MapBody body;
        public RectTransform rt;
    }

    private RawImage mapImage;
    private RectTransform mapRT;
    private RectTransform selectionRing;
    private RectTransform subLocRow;
    private List<HitRegion> hitRegions = new List<HitRegion>();
    private float lastCanvasW = -1f;
    private float lastCanvasH = -1f;

    private void BuildMapBackground(RectTransform parent) {
        GameObject go = new GameObject("MapBackground");
        go.transform.SetParent(parent, false);

        mapRT = go.AddComponent<RectTransform>();
        mapRT.anchorMin = Vector2.zero;
        mapRT.anchorMax = Vector2.zero;
        mapRT.pivot = new Vector2(0.5f, 1f);   // positioned by its top centre

        mapImage = go.AddComponent<RawImage>();
        Texture2D tex = Resources.Load<Texture2D>("Textures/SolarMap");
        mapImage.texture = tex;
        mapImage.color = tex != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        mapImage.raycastTarget = false;

        if(tex == null)
            Debug.LogWarning("[SystemMapUI] Resources/Textures/SolarMap not found.");

        BuildHitRegions();
        BuildSelectionIndicator();
        BuildSubLocationRow();

        // Behind every panel.
        go.transform.SetAsFirstSibling();
        LayoutMapBackground();
    }

    // Fits the artwork into the band the sidebar does not cover, held to the
    // top so the planets clear the report panel along the bottom edge.
    private void LayoutMapBackground() {
        if(mapRT == null || canvasRT == null) return;

        float canvasW = canvasRT.rect.width;
        float canvasH = canvasRT.rect.height;
        if(canvasW <= 0f || canvasH <= 0f) return;

        float left  = LeftReservedUnits + Margin;
        float right = canvasW - Margin;
        float topY  = canvasH - 20f;

        float boxW = Mathf.Max(64f, right - left);
        float boxH = Mathf.Max(64f, topY - 90f);

        float w = Mathf.Min(boxW, boxH * MapAspect);
        float h = w / MapAspect;

        mapRT.sizeDelta = new Vector2(w, h);
        mapRT.anchoredPosition = new Vector2((left + right) * 0.5f, topY);

        foreach(HitRegion hr in hitRegions) {
            float d = Mathf.Max(MinHitSize, hr.body.nr * 2f * w);
            hr.rt.sizeDelta = new Vector2(d, d);
        }

        lastCanvasW = canvasW;
        lastCanvasH = canvasH;
    }

    private void BuildHitRegions() {
        foreach(MapBody body in MapBodies) {
            GameObject go = new GameObject("Hit_" + body.env);
            go.transform.SetParent(mapRT, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            // Anchored in normalised map space, so they track any rescale.
            rt.anchorMin = new Vector2(body.ncx, 1f - body.ncy);
            rt.anchorMax = new Vector2(body.ncx, 1f - body.ncy);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;

            // Transparent, but still a raycast target: the button tint gives a
            // faint hover glow so the planets read as clickable.
            Image img = go.AddComponent<Image>();
            img.color = Color.white;

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0f);
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            cb.pressedColor = new Color(1f, 1f, 1f, 0.32f);
            cb.selectedColor = new Color(1f, 1f, 1f, 0f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;

            SpaceEnvironment captured = body.env;
            btn.onClick.AddListener(() => SelectBody(captured));

            HitRegion hr = new HitRegion();
            hr.body = body;
            hr.rt = rt;
            hitRegions.Add(hr);
        }
    }

    private HitRegion FindHit(SpaceEnvironment env) {
        foreach(HitRegion hr in hitRegions) if(hr.body.env == env) return hr;
        return null;
    }

    private void SelectBody(SpaceEnvironment env) {
        GameManager.Instance.SelectLocation(env);
        ShowSubLocations(env);
    }

    private void BuildSelectionIndicator() {
        GameObject go = new GameObject("SelectionRing");
        go.transform.SetParent(mapRT, false);

        selectionRing = go.AddComponent<RectTransform>();
        selectionRing.pivot = new Vector2(0.5f, 0.5f);

        Image img = go.AddComponent<Image>();
        img.sprite = DashedRingSprite();
        img.color = Color.white;
        img.raycastTarget = false;

        go.SetActive(false);
    }

    private void UpdateSelectionIndicator() {
        if(selectionRing == null || mapRT == null) return;

        GameManager gm = GameManager.Instance;
        HitRegion hr = (gm != null && gm.CurrentSelection.HasValue)
            ? FindHit(gm.CurrentSelection.Value) : null;

        if(hr == null) {
            if(selectionRing.gameObject.activeSelf) selectionRing.gameObject.SetActive(false);
            return;
        }

        if(!selectionRing.gameObject.activeSelf) selectionRing.gameObject.SetActive(true);

        selectionRing.anchorMin = hr.rt.anchorMin;
        selectionRing.anchorMax = hr.rt.anchorMax;
        selectionRing.anchoredPosition = Vector2.zero;

        float d = Mathf.Max(MinHitSize + 14f, hr.body.nr * 2f * mapRT.sizeDelta.x * 1.5f);
        selectionRing.sizeDelta = new Vector2(d, d);
        selectionRing.Rotate(0f, 0f, -18f * Time.deltaTime);
    }

    // ---- Sub-locations ----------------------------------------------------
    // Moons no longer exist as scene objects, so they get a small button stack
    // under whichever planet is selected.

    private void BuildSubLocationRow() {
        GameObject go = new GameObject("SubLocations");
        go.transform.SetParent(mapRT, false);

        subLocRow = go.AddComponent<RectTransform>();
        subLocRow.pivot = new Vector2(0.5f, 1f);
        subLocRow.sizeDelta = new Vector2(124f, 0f);

        VerticalLayoutGroup vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        go.SetActive(false);
    }

    private void ShowSubLocations(SpaceEnvironment env) {
        if(subLocRow == null) return;

        for(int i = subLocRow.childCount - 1; i >= 0; i--)
            Destroy(subLocRow.GetChild(i).gameObject);

        List<SubLocation> subs = SubLocationDatabase.GetSubLocations(env);
        if(subs.Count == 0) {
            subLocRow.gameObject.SetActive(false);
            return;
        }

        subLocRow.gameObject.SetActive(true);

        HitRegion hr = FindHit(env);
        if(hr != null) {
            subLocRow.anchorMin = hr.rt.anchorMin;
            subLocRow.anchorMax = hr.rt.anchorMax;
            subLocRow.anchoredPosition = new Vector2(0f, -(hr.rt.sizeDelta.y * 0.5f + 10f));
        }

        SpaceEnvironment captured = env;
        AddSubLocButton("ORBIT", () => GameManager.Instance.SelectLocation(captured));

        foreach(SubLocation s in subs) {
            string subName = s.name;
            AddSubLocButton(subName.ToUpper(), () => {
                GameManager.Instance.SelectLocation(captured);
                GameManager.Instance.SelectSubLocation(subName);
            });
        }
    }

    private void AddSubLocButton(string label, Action onClick) {
        GameObject go = CreateChoiceButton(subLocRow, "Sub_" + label, label, onClick);
        SetHeight(go.transform, 24);
        go.GetComponent<Image>().color = ButtonNormal;

        TMP_Text txt = go.GetComponentInChildren<TMP_Text>();
        txt.color = GoldText;
        txt.fontSize = 12f;
    }

    // Anti-aliased dashed ring, used to mark the selected planet.
    private static Sprite dashedRingSprite;

    private static Sprite DashedRingSprite() {
        if(dashedRingSprite != null) return dashedRingSprite;

        const int size = 128;
        const int dashes = 10;
        const float duty = 0.6f;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float c = size * 0.5f;
        float outer = c - 1.5f;
        float inner = outer * 0.86f;
        Color[] px = new Color[size * size];

        for(int y = 0; y < size; y++) {
            for(int x = 0; x < size; x++) {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;

                if(d <= outer && d >= inner) {
                    float ang = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f);
                    if(ang < 0f) ang += 1f;
                    float seg = ang * dashes;
                    if(seg - Mathf.Floor(seg) < duty)
                        a = Mathf.Clamp01(Mathf.Min(outer - d, d - inner));
                }
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        dashedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return dashedRingSprite;
    }

    private void SetupPreviewScene() {
        previewRenderTex = new RenderTexture(512, 320, 16);
        previewRenderTex.antiAliasing = 2;

        GameObject camGO = new GameObject("PreviewCamera");
        camGO.transform.position = new Vector3(1000, 1000, 995);
        camGO.transform.rotation = Quaternion.identity;
        previewCamera = camGO.AddComponent<Camera>();
        previewCamera.targetTexture = previewRenderTex;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.03f, 0.03f, 0.06f, 1f);
        previewCamera.fieldOfView = 20;
        previewCamera.cullingMask = LayerMask.GetMask("Default");
        previewCamera.depth = -10;

        GameObject lightGO = new GameObject("PreviewLight");
        lightGO.transform.position = new Vector3(998, 1003, 993);
        lightGO.transform.rotation = Quaternion.Euler(30, -30, 0);
        Light previewLight = lightGO.AddComponent<Light>();
        previewLight.type = LightType.Directional;
        previewLight.intensity = 1.2f;
        previewLight.color = new Color(1f, 0.95f, 0.85f);

        Vector3 center = new Vector3(1000, 1000, 1000);
        tierPreviewObjects = new GameObject[4];

        tierPreviewObjects[0] = LoadErrorTextModel(center);
        if(tierPreviewObjects[0] == null) {
            GameObject noneParent = new GameObject("Preview_None");
            noneParent.transform.position = center;
            GameObject noneSphere = CreatePreviewPrimitive(PrimitiveType.Sphere, center, 0.7f,
                new Color(0.7f, 0.15f, 0.1f), "NoneSphere", true);
            noneSphere.transform.SetParent(noneParent.transform, true);
            tierPreviewObjects[0] = noneParent;
        }

        tierPreviewObjects[1] = CreatePongPreview(center);

        tierPreviewObjects[2] = LoadPreviewModel("Models/Mario/mario", center, 0.015f, 0.45f);
        if(tierPreviewObjects[2] == null)
            tierPreviewObjects[2] = CreatePreviewPrimitive(PrimitiveType.Sphere, center, 0.8f,
                new Color(0.3f, 0.5f, 0.3f), "Preview_Med", false);

        tierPreviewObjects[3] = LoadPreviewModel("Models/DoomSlayer/doommarine", center, 0.008f, 0.28f);
        if(tierPreviewObjects[3] == null) {
            GameObject highParent = new GameObject("Preview_High");
            highParent.transform.position = center;
            GameObject highSphere = CreatePreviewPrimitive(PrimitiveType.Sphere, center, 0.8f,
                new Color(0.9f, 0.75f, 0.3f), "HighSphere", true);
            highSphere.transform.SetParent(highParent.transform, true);
            tierPreviewObjects[3] = highParent;
        }

        for(int i = 0; i < tierPreviewObjects.Length; i++) {
            tierPreviewObjects[i].SetActive(false);
        }

        if(previewDisplay != null) {
            previewDisplay.texture = previewRenderTex;
        }
    }

    private GameObject CreatePreviewPrimitive(PrimitiveType type, Vector3 position, float scale, Color color, string name, bool emissive) {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * scale;

        Collider col = obj.GetComponent<Collider>();
        if(col != null) Destroy(col);

        Renderer rend = obj.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        if(emissive) {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.8f);
        }
        rend.material = mat;
        return obj;
    }

    private GameObject CreatePongPreview(Vector3 center) {
        GameObject root = new GameObject("Preview_Pong");
        root.transform.position = center;

        Shader unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material whiteMat = new Material(unlitShader);
        whiteMat.color = Color.white;

        GameObject leftPaddle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftPaddle.name = "Paddle_L";
        leftPaddle.transform.SetParent(root.transform, false);
        leftPaddle.transform.localPosition = new Vector3(-0.6f, 0f, 0f);
        leftPaddle.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
        leftPaddle.GetComponent<Renderer>().material = whiteMat;
        Destroy(leftPaddle.GetComponent<Collider>());

        GameObject rightPaddle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightPaddle.name = "Paddle_R";
        rightPaddle.transform.SetParent(root.transform, false);
        rightPaddle.transform.localPosition = new Vector3(0.6f, 0f, 0f);
        rightPaddle.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
        rightPaddle.GetComponent<Renderer>().material = whiteMat;
        Destroy(rightPaddle.GetComponent<Collider>());

        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Ball";
        ball.transform.SetParent(root.transform, false);
        ball.transform.localPosition = new Vector3(0.15f, 0.1f, 0f);
        ball.transform.localScale = new Vector3(0.07f, 0.07f, 0.07f);
        ball.GetComponent<Renderer>().material = whiteMat;
        Destroy(ball.GetComponent<Collider>());

        for(int i = 0; i < 5; i++) {
            GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dash.name = "Dash_" + i;
            dash.transform.SetParent(root.transform, false);
            dash.transform.localPosition = new Vector3(0f, -0.4f + i * 0.2f, 0f);
            dash.transform.localScale = new Vector3(0.03f, 0.08f, 0.03f);
            dash.GetComponent<Renderer>().material = whiteMat;
            Destroy(dash.GetComponent<Collider>());
        }

        return root;
    }

    private GameObject LoadErrorTextModel(Vector3 center) {
        GameObject prefab = Resources.Load<GameObject>("Models/ErrorText/ERRORText");
        if(prefab == null) return null;

        GameObject instance = Instantiate(prefab);
        instance.name = "Preview_ErrorText";

        foreach(Collider col in instance.GetComponentsInChildren<Collider>())
            Destroy(col);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if(renderers.Length == 0) {
            Destroy(instance);
            return null;
        }

        ApplyModelTextures(instance, "Models/ErrorText/ERRORText");

        instance.transform.position = center;
        instance.transform.localScale = Vector3.one;

        Bounds bounds = new Bounds(center, Vector3.zero);
        foreach(Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if(maxDim > 0.001f) {
            float targetSize = 1.2f;
            float s = targetSize / maxDim;
            instance.transform.localScale = Vector3.one * s;
        }

        bounds = new Bounds(center, Vector3.zero);
        foreach(Renderer r in instance.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(r.bounds);
        Vector3 offset = center - bounds.center;
        instance.transform.position += offset;

        return instance;
    }

    private GameObject LoadPreviewModel(string resourcePath, Vector3 position, float scale, float bustFraction) {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if(prefab == null) return null;

        GameObject instance = Instantiate(prefab);
        instance.name = "Preview_" + prefab.name;
        instance.transform.position = position;
        instance.transform.localScale = Vector3.one * scale;

        foreach(Collider col in instance.GetComponentsInChildren<Collider>())
            Destroy(col);

        if(resourcePath.Contains("mario") || resourcePath.Contains("Mario")) {
            foreach(Transform child in instance.GetComponentsInChildren<Transform>(true)) {
                string n = child.name.ToLower();
                if(n.Contains("unused") || n.Contains("wing") || n.Contains("metal") || n.Contains("logo") || n.Contains("right_hand_cap"))
                    child.gameObject.SetActive(false);
            }
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if(renderers.Length == 0) {
            Destroy(instance);
            return null;
        }

        ApplyModelTextures(instance, resourcePath);

        Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
        foreach(Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        if(bounds.size.magnitude > 0.01f) {
            float fullHeight = bounds.size.y;
            float bustTop = bounds.max.y;
            float bustBottom = bustTop - fullHeight * bustFraction;
            float bustHeight = bustTop - bustBottom;
            float bustCenterY = bustBottom + bustHeight * 0.5f;

            float maxExtent = Mathf.Max(bounds.extents.x, bustHeight * 0.5f, bounds.extents.z);
            if(maxExtent > 0) {
                float desiredSize = 0.9f;
                float adjustedScale = (desiredSize / maxExtent) * instance.transform.localScale.x;
                instance.transform.localScale = Vector3.one * adjustedScale;
            }

            bounds = new Bounds(instance.transform.position, Vector3.zero);
            foreach(Renderer r in instance.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);

            fullHeight = bounds.size.y;
            bustTop = bounds.max.y;
            bustBottom = bustTop - fullHeight * bustFraction;
            bustCenterY = bustBottom + (bustTop - bustBottom) * 0.5f;

            Vector3 offset = position - new Vector3(bounds.center.x, bustCenterY, bounds.center.z);
            instance.transform.position += offset;
        }

        return instance;
    }

    private void ApplyModelTextures(GameObject instance, string resourcePath) {
        string folder = resourcePath.Substring(0, resourcePath.LastIndexOf('/') + 1);

        foreach(Renderer rend in instance.GetComponentsInChildren<Renderer>()) {
            foreach(Material mat in rend.materials) {
                if(mat.mainTexture != null) continue;

                string matName = mat.name.Replace(" (Instance)", "");

                string texName = null;
                if(matName.Contains("GIGN_DMBASE2")) texName = "GIGN_DMBASE2";
                else if(matName.Contains("Backpack2")) texName = "Backpack2";
                else if(matName.Contains("doommarine_arms")) texName = "models_characters_doommarine_doommarine_arms_c";
                else if(matName.Contains("doommarine_cowl")) texName = "models_characters_doommarine_doommarine_cowl_c";
                else if(matName.Contains("doommarine_helmet")) texName = "models_characters_doommarine_doommarine_helmet_c";
                else if(matName.Contains("doommarine_legs")) texName = "models_characters_doommarine_doommarine_legs_c";
                else if(matName.Contains("doommarine_torso")) texName = "models_characters_doommarine_doommarine_torso_c";
                else if(matName.Contains("doommarine_visor")) texName = "models_characters_doommarine_doommarine_visor_c";
                else if(matName.Contains("typeBlinn") || resourcePath.Contains("ErrorText")) texName = "ERRORText_typeBlinn_BaseColor";

                if(texName != null) {
                    Texture2D tex = Resources.Load<Texture2D>(folder + texName);
                    if(tex != null) {
                        mat.mainTexture = tex;
                        if(texName.Contains("ERRORText")) {
                            Texture2D emTex = Resources.Load<Texture2D>(folder + "ERRORText_typeBlinn_Emissive");
                            if(emTex != null) {
                                mat.EnableKeyword("_EMISSION");
                                mat.SetTexture("_EmissionMap", emTex);
                                mat.SetColor("_EmissionColor", Color.white);
                            }
                        }
                    }
                }
            }
        }

        if(resourcePath.Contains("Mario") || resourcePath.Contains("mario")) {
            foreach(Transform child in instance.GetComponentsInChildren<Transform>()) {
                string n = child.name.ToLower();
                if(n.Contains("unused") || n.Contains("wing") || n.Contains("metal") || n.Contains("logo")) {
                    child.gameObject.SetActive(false);
                }
            }
        }
    }

    private void ShowPreviewTier(int vrTierLevel) {
        if(tierPreviewObjects == null) return;

        // vrTierLevel < 0 means "nothing selected yet" - hide every model.
        for(int i = 0; i < tierPreviewObjects.Length; i++) {
            if(tierPreviewObjects[i] != null)
                tierPreviewObjects[i].SetActive(i == vrTierLevel);
        }

        if(previewCamera != null)
            previewCamera.backgroundColor = vrTierLevel == 1
                ? Color.black
                : new Color(0.03f, 0.03f, 0.06f, 1f);

        if(previewLabel != null) {
            previewLabel.text = vrTierLevel < 0
                ? "FIDELITY: --"
                : "FIDELITY: " + RadiationCalculator.GetTierShortName(vrTierLevel);
        }
    }

    private void RefreshVRPreview() {
        GameManager gm = GameManager.Instance;
        if(gm == null || !gm.CurrentSelection.HasValue) {
            ShowPreviewTier(-1);
            return;
        }
        ShowPreviewTier(gm.CurrentResult.tierLevel);
    }

    // ---- Model events ------------------------------------------------------

    private void OnLocationChanged(SpaceEnvironment env) {
        selectionNameText.text = env.ToString().ToUpper();
        selectionTypeText.text = "Planetary orbit";
        warningText.text = "";
        ResetStats();
    }

    private void OnSubLocationChanged(string subName) {
        if(string.IsNullOrEmpty(subName)) return;
        selectionNameText.text = subName.ToUpper();
        selectionTypeText.text = "Moon / sub-location";
    }

    private void OnFidelityChanged(FidelityResult result) {
        GameManager gm = GameManager.Instance;
        if(gm == null || !gm.CurrentSelection.HasValue) return;

        bool isMoon = !string.IsNullOrEmpty(gm.CurrentSubLocation);
        string locName = isMoon
            ? gm.CurrentSubLocation.ToUpper()
            : gm.CurrentSelection.Value.ToString().ToUpper();

        // These flags tell us how far Calculate() got before returning.
        bool hasEnvData = !string.IsNullOrEmpty(result.dominantSource);
        bool hasHwData = result.tidToleranceKrad > 0f;

        string lifespan = RadiationCalculator.FormatLifespan(result.lifespanYears);
        bool willFail = result.lifespanYears > 0f
            && result.lifespanYears < result.missionDurationYears;

        selectionNameText.text = locName;
        selectionTypeText.text = (isMoon ? "Moon / sub-location" : "Planetary orbit")
            + "   -   " + result.missionDurationYears + " year mission";

        ResetStats();

        if(hasEnvData) {
            SetStat("env", result.locationName);
            SetStat("source", result.dominantSource);
            SetStat("conf", string.IsNullOrEmpty(result.confidence)
                ? "--" : result.confidence.ToUpper());

            if(result.baselineTID >= 0f) {
                string range = result.tidMax > result.tidMin
                    ? string.Format("  ({0:G3}-{1:G3})", result.tidMin, result.tidMax)
                    : "";
                SetStat("basetid", string.Format("{0:G3} krad/yr{1}", result.baselineTID, range));
            } else {
                SetStat("basetid", "unknown");
            }

            SetStat("shield", string.Format("{0}   x{1:0.00}",
                result.shieldingName, result.shieldingFactor));
            SetStat("efftid", string.Format("{0:G3} krad/yr", result.effectiveTID));
            SetStat("dose", string.Format("{0:G3} krad", result.totalMissionDose));
        }

        if(hasHwData) {
            SetStat("hw", result.hardwareName + "   "
                + RadiationCalculator.FormatGFLOPS(result.hardwareGFLOPS) + " GF");
            SetStat("tol", string.Format("{0:G3} krad", result.tidToleranceKrad));
        }

        SetStat("life", willFail
            ? "<color=" + WarnHex + ">" + lifespan + "</color>"
            : lifespan);

        SetStat("tier", result.hardwareSurvives
            ? result.tierName + " (" + result.tierShortName + ")"
            : "<color=" + WarnHex + ">" + result.tierName + "</color>");

        if(!hasEnvData) {
            warningText.text = "<color=" + WarnHex + ">No dosimetry data for this location.</color>";
        } else if(!result.hardwareSurvives) {
            warningText.text = "<color=" + WarnHex + ">Total dose exceeds hardware tolerance.</color>";
        } else if(willFail) {
            warningText.text = "<color=" + WarnHex + ">Fails at year "
                + Mathf.FloorToInt(result.lifespanYears)
                + " of " + result.missionDurationYears + ".</color>";
        } else {
            warningText.text = "<color=#7FCE7F>Hardware survives the full mission.</color>";
        }

        HighlightTier(result.tierLevel);
        RefreshVRPreview();
    }

    // ---- Small builders ----------------------------------------------------

    private RectTransform CreatePanel(RectTransform parent, string name, Color color, bool edge) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();

        Image img = go.AddComponent<Image>();
        img.color = color;

        if(edge) {
            Outline o = go.AddComponent<Outline>();
            o.effectColor = PanelEdge;
            o.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return rt;
    }

    private TMP_Text CreateText(RectTransform parent, string name, string text, float fontSize, Color color) {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.fontStyle = FontStyles.Normal;
        tmp.richText = true;
        tmp.raycastTarget = false;

        if(go.GetComponent<LayoutElement>() == null) {
            go.AddComponent<LayoutElement>();
        }
        return tmp;
    }

    private void CreateSeparator(RectTransform parent) {
        GameObject go = new GameObject("Separator");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        Image img = go.AddComponent<Image>();
        img.color = new Color(GoldText.r, GoldText.g, GoldText.b, 0.35f);
        img.raycastTarget = false;

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.minHeight = 2;
        le.preferredHeight = 2;
    }

    // Anti-aliased white disc, used for the info button.
    private static Sprite circleSprite;

    private static Sprite CircleSprite() {
        if(circleSprite != null) return circleSprite;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        float r = size * 0.5f;
        Color[] px = new Color[size * size];

        for(int y = 0; y < size; y++) {
            for(int x = 0; x < size; x++) {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return circleSprite;
    }

    // Pin both min and preferred so a layout group can never quietly squeeze a
    // box below the height its font actually needs.
    private static void SetHeight(Component c, float h) {
        LayoutElement le = c.GetComponent<LayoutElement>();
        if(le == null) le = c.gameObject.AddComponent<LayoutElement>();
        le.minHeight = h;
        le.preferredHeight = h;
    }

    private static void Stretch(RectTransform rt) {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
