﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.XR.Simulation
{
    /// <summary>
    /// Toolbar overlay for managing AR environments from the Scene view.
    /// </summary>
    [Icon(arEnvironmentIconPath)]
    [Overlay(typeof(SceneView), overlayId, toolbarDisplayName)]
    class AREnvironmentToolbarOverlay : ToolbarOverlay
    {
        const string k_IconsPath = "Packages/com.unity.xr.arfoundation/Editor/Icons/";

        public const string toolbarDisplayName = "AR Environment";
        public const string overlayId = "AREnvironmentToolbar";
        public const string arEnvironmentIconPath = k_IconsPath + "AREnvironment.png";
        public const string arrowCaretLeftIconPath = k_IconsPath + "ArrowCaretLeft.png";
        public const string arrowCaretRightIconPath = k_IconsPath + "ArrowCaretRight.png";
        public const string addEditIconPath = k_IconsPath + "AddEdit.png";

        public static Func<string[], string[]> collectElementIds;

        public static bool CanEnableContent(EditorWindow window)
        {
            return AREnvironmentViewUtilities.IsBaseSceneView(window)
                && !EditorApplication.isPlayingOrWillChangePlaymode
                && SimulationEditorUtilities.simulationSubsystemEnabled
                && PrefabStageUtility.GetCurrentPrefabStage() == null;
        }

        static string[] GetElementIds()
        {
            var elementIds = new[]
            {
                HiddenToolbarTrackingElement.id,
                EnvironmentDropdown.id,
                PreviousEnvironmentButton.id,
                NextEnvironmentButton.id,
                AddEditEnvironmentDropdown.id
            };

            if (collectElementIds != null)
                elementIds = collectElementIds(elementIds);

            return elementIds;
        }

        void OnDisplayedChanged(bool value)
        {
            if (containerWindow is SceneView sceneView)
            {
                if (value)
                    AREnvironmentView.instance.AddEnvironmentView(sceneView);
                else
                    AREnvironmentView.instance.RemoveEnvironmentView(sceneView);
            }
        }

        AREnvironmentToolbarOverlay() : base(GetElementIds()) { }

        public override void OnCreated()
        {
            base.OnCreated();

            displayedChanged += OnDisplayedChanged;

            var assetGuid = SimulationEnvironmentAssetsManager.GetActiveEnvironmentAssetGuid();

            AREditorAnalytics.simulationUIAnalyticsEvent.Send(
                new SimulationUIAnalyticsArgs(
                    eventName: SimulationUIAnalyticsArgs.EventName.WindowUsed,
                    environmentGuid: assetGuid,
                    windowUsed: new SimulationUIAnalyticsArgs.WindowUsed { name = toolbarDisplayName, isActive = true }));
        }

        public override void OnWillBeDestroyed()
        {
            displayedChanged -= OnDisplayedChanged;
            OnDisplayedChanged(false);

            base.OnWillBeDestroyed();

            var assetGuid = SimulationEnvironmentAssetsManager.GetActiveEnvironmentAssetGuid();

            AREditorAnalytics.simulationUIAnalyticsEvent.Send(
                new SimulationUIAnalyticsArgs(
                    eventName: SimulationUIAnalyticsArgs.EventName.WindowUsed,
                    environmentGuid: assetGuid,
                    windowUsed: new SimulationUIAnalyticsArgs.WindowUsed { name = toolbarDisplayName, isActive = false }));
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class HiddenToolbarTrackingElement : VisualElement
    {
        public const string id = AREnvironmentToolbarOverlay.overlayId + "/HiddenTracker";

        public HiddenToolbarTrackingElement()
        {
            // Create a hidden element to track Simulation loader being enabled or disabled
            Add(AREnvironmentViewUtilities.TrackStandAloneXRSettingsChange());
        }
    }

    abstract class BaseEnvironmentDropdown : EditorToolbarDropdown, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }

        protected BaseEnvironmentDropdown()
        {
            clicked += ToggleDropdown;
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            UpdateEnabled();
        }

        protected virtual void OnAttachToPanel(AttachToPanelEvent evt)
        {
            UpdateEnabled();
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            SimulationEditorUtilities.simulationSubsystemLoaderAddedOrRemoved += UpdateEnabled;
        }

        protected virtual void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            SimulationEditorUtilities.simulationSubsystemLoaderAddedOrRemoved -= UpdateEnabled;
        }

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            UpdateEnabled();
        }

        void OnPrefabStageChanged(PrefabStage prefabStage)
        {
            UpdateEnabled();
        }

        protected abstract void ToggleDropdown();

        void UpdateEnabled()
        {
            SetEnabled(AREnvironmentToolbarOverlay.CanEnableContent(containerWindow));
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class EnvironmentDropdown : BaseEnvironmentDropdown
    {
        static readonly List<string> k_EnvironmentMenuItemNames = new();
        const string k_Tooltip = "Change the environment.";
        const string k_NoEnvironmentText = "No environment set";
        const string k_InstallEnvironmentsText = "Install sample environments";
        const string k_ImportEnvironmentsText = "Import sample environments";
        const int k_HorizontalLayoutWidth = 160;

        public const string id = AREnvironmentToolbarOverlay.overlayId + "/EnvironmentDropdown";

        Overlay m_ContainerOverlay;

        public EnvironmentDropdown()
        {
            style.whiteSpace = WhiteSpace.NoWrap;
            tooltip = k_Tooltip;
            var iconContent = EditorGUIUtility.TrIconContent(AREnvironmentToolbarOverlay.arEnvironmentIconPath);
            icon = iconContent.image as Texture2D;
        }

        protected override void OnAttachToPanel(AttachToPanelEvent evt)
        {
            base.OnAttachToPanel(evt);
            UpdateText();
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            environmentsManager.activeEnvironmentChanged += UpdateText;

            // Display text when overlay is in horizontal layout, otherwise hide the text
            if (containerWindow.TryGetOverlay(AREnvironmentToolbarOverlay.overlayId, out m_ContainerOverlay))
            {
                UpdateFromOverlayLayout(m_ContainerOverlay.layout);
                m_ContainerOverlay.layoutChanged += UpdateFromOverlayLayout;
            }
        }

        protected override void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            base.OnDetachFromPanel(evt);
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            environmentsManager.activeEnvironmentChanged -= UpdateText;

            if (m_ContainerOverlay != null)
                m_ContainerOverlay.layoutChanged -= UpdateFromOverlayLayout;
        }

        void UpdateFromOverlayLayout(Layout overlayLayout)
        {
            if (overlayLayout.HasFlag(Layout.VerticalToolbar))
            {
                style.width = StyleKeyword.Auto;
                foreach (var childElement in Children())
                {
                    if (childElement is TextElement)
                    {
                        childElement.style.display = DisplayStyle.None;
                        break;
                    }
                }
            }
            else
            {
                style.width = k_HorizontalLayoutWidth;
                foreach (var childElement in Children())
                {
                    if (childElement is TextElement)
                    {
                        childElement.style.display = DisplayStyle.Flex;
                        break;
                    }
                }
            }
        }

        protected override void ToggleDropdown()
        {
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            var activeEnvironmentIndex = environmentsManager.GetActiveEnvironmentIndex();
            var menu = new GenericMenu();
            k_EnvironmentMenuItemNames.Clear();
            environmentsManager.GetAllEnvironmentMenuItemNames(k_EnvironmentMenuItemNames);
            for (var i = 0; i < k_EnvironmentMenuItemNames.Count; i++)
            {
                menu.AddItem(
                    new GUIContent(k_EnvironmentMenuItemNames[i]),
                    activeEnvironmentIndex == i,
                    OnMenuItemSelected,
                    i);
            }

            if (k_EnvironmentMenuItemNames.Count > 0)
                menu.AddSeparator("");

            var samplesExist = SampleEnvironmentsHelper.FindPackageSamples().Any();
            var installGuiContent = new GUIContent(samplesExist ? k_ImportEnvironmentsText : k_InstallEnvironmentsText);
            if (SampleEnvironmentsHelper.processingInstallRequest)
            {
                menu.AddDisabledItem(installGuiContent);
            }
            else
            {
                menu.AddItem(
                    installGuiContent,
                    false,
                    SampleEnvironmentsHelper.InstallSampleEnvironments);
            }

            menu.DropDown(worldBound);
        }

        static void OnMenuItemSelected(object data)
        {
            var index = (int)data;
            SimulationEnvironmentAssetsManager.Instance.SelectEnvironmentAtIndex(index);
        }

        void UpdateText()
        {
            var activeEnvironmentName = SimulationEnvironmentAssetsManager.Instance.GetActiveEnvironmentDisplayName();
            text = string.IsNullOrEmpty(activeEnvironmentName) ? k_NoEnvironmentText : activeEnvironmentName;
        }
    }

    abstract class EnvironmentCycleButton : EditorToolbarButton, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }

        protected EnvironmentCycleButton()
        {
            clicked += OnClicked;
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            UpdateEnabled();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            UpdateEnabled();
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            environmentsManager.availableEnvironmentsChanged += UpdateEnabled;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            PrefabStage.prefabStageOpened += OnPrefabStageChanged;
            PrefabStage.prefabStageClosing += OnPrefabStageChanged;
            SimulationEditorUtilities.simulationSubsystemLoaderAddedOrRemoved += UpdateEnabled;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            environmentsManager.availableEnvironmentsChanged -= UpdateEnabled;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
            PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
            SimulationEditorUtilities.simulationSubsystemLoaderAddedOrRemoved -= UpdateEnabled;
        }

        void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            UpdateEnabled();
        }

        void OnPrefabStageChanged(PrefabStage prefabStage)
        {
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            SetEnabled(SimulationEnvironmentAssetsManager.Instance.environmentsCount > 1
                && AREnvironmentToolbarOverlay.CanEnableContent(containerWindow));
        }

        void OnClicked()
        {
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            var environmentsCount = environmentsManager.environmentsCount;
            if (environmentsCount < 2)
                return;

            var currentEnvIndex = environmentsManager.GetActiveEnvironmentIndex();
            var selectEnvIndex = GetSelectIndex(currentEnvIndex, environmentsCount);
            environmentsManager.SelectEnvironmentAtIndex(selectEnvIndex);
        }

        protected abstract int GetSelectIndex(int currentEnvironmentIndex, int environmentsCount);
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class PreviousEnvironmentButton : EnvironmentCycleButton
    {
        const string k_Tooltip = "Load the previous environment.";

        public const string id = AREnvironmentToolbarOverlay.overlayId + "/PreviousEnvironment";

        public PreviousEnvironmentButton()
        {
            tooltip = k_Tooltip;
            var iconContent = EditorGUIUtility.TrIconContent(AREnvironmentToolbarOverlay.arrowCaretLeftIconPath);
            icon = iconContent.image as Texture2D;
        }

        protected override int GetSelectIndex(int currentEnvironmentIndex, int environmentsCount)
        {
            return currentEnvironmentIndex == 0 ? environmentsCount - 1 : currentEnvironmentIndex - 1;
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class NextEnvironmentButton : EnvironmentCycleButton
    {
        const string k_Tooltip = "Load the next environment.";

        public const string id = AREnvironmentToolbarOverlay.overlayId + "/NextEnvironment";

        public NextEnvironmentButton()
        {
            tooltip = k_Tooltip;
            var iconContent = EditorGUIUtility.TrIconContent(AREnvironmentToolbarOverlay.arrowCaretRightIconPath);
            icon = iconContent.image as Texture2D;
        }

        protected override int GetSelectIndex(int currentEnvironmentIndex, int environmentsCount)
        {
            return currentEnvironmentIndex == environmentsCount - 1 ? 0 : currentEnvironmentIndex + 1;
        }
    }

    [EditorToolbarElement(id, typeof(SceneView))]
    class AddEditEnvironmentDropdown : BaseEnvironmentDropdown
    {
        const string k_Tooltip = "Show actions for environment creation.";
        const string k_CreateEnvironmentText = "Create environment";
        const string k_DuplicateEnvironmentText = "Duplicate environment";
        const string k_EditEnvironmentText = "Edit environment";
        const string k_AssetsPath = "Assets";

        public const string id = AREnvironmentToolbarOverlay.overlayId + "/AddEditEnvironmentDropdown";

        public AddEditEnvironmentDropdown()
        {
            tooltip = k_Tooltip;
            var iconContent = EditorGUIUtility.TrIconContent(AREnvironmentToolbarOverlay.addEditIconPath);
            icon = iconContent.image as Texture2D;
        }

        protected override void ToggleDropdown()
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(k_CreateEnvironmentText),
                false,
                OnCreateEnvironmentSelected);

            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            var anyEnvironmentActive = environmentsManager.activeEnvironmentExists;
            if (anyEnvironmentActive)
            {
                menu.AddItem(
                    new GUIContent(k_DuplicateEnvironmentText),
                    false,
                    OnDuplicateEnvironmentSelected);
            }
            else
                menu.AddDisabledItem(new GUIContent(k_DuplicateEnvironmentText));

            if (anyEnvironmentActive && environmentsManager.IsActiveEnvironmentEditable())
            {
                menu.AddItem(
                    new GUIContent(k_EditEnvironmentText),
                    false,
                    OnEditEnvironmentSelected);
            }
            else
                menu.AddDisabledItem(new GUIContent(k_EditEnvironmentText));

            menu.DropDown(worldBound);
        }

        static void OnCreateEnvironmentSelected()
        {
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            var defaultSavePath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(k_AssetsPath,
                SimulationEnvironmentAssetsManager.newEnvironmentFileName));

            var defaultSaveName = Path.GetFileName(defaultSavePath);
            var environmentExtension = Path.GetExtension(defaultSaveName).Substring(1); // Remove '.' for use with SaveFilePanelInProject
            var saveFilePath = EditorUtility.SaveFilePanelInProject("Save environment", defaultSaveName, environmentExtension, "");
            if (string.IsNullOrEmpty(saveFilePath))
                return;

            if (!environmentsManager.TryCreateEnvironment(saveFilePath, out var newEnvironmentIndex))
                return;

            environmentsManager.SelectEnvironmentAtIndex(newEnvironmentIndex);
            environmentsManager.OpenActiveEnvironmentForEditing();
        }

        static void OnDuplicateEnvironmentSelected()
        {
            var environmentsManager = SimulationEnvironmentAssetsManager.Instance;
            var activeEnvironmentPath = environmentsManager.GetActiveEnvironmentPath();
            var activeEnvironmentDirectory = Path.GetDirectoryName(activeEnvironmentPath);
            var defaultSaveName = Path.GetFileName(AssetDatabase.GenerateUniqueAssetPath(activeEnvironmentPath));
            var defaultSaveDirectory = activeEnvironmentDirectory.StartsWith(k_AssetsPath) ? activeEnvironmentDirectory : k_AssetsPath;
            var environmentExtension = Path.GetExtension(activeEnvironmentPath).Substring(1); // Remove '.' for use with SaveFilePanelInProject
            var saveFilePath = EditorUtility.SaveFilePanelInProject("Save environment", defaultSaveName,
                environmentExtension, "", defaultSaveDirectory);

            if (string.IsNullOrEmpty(saveFilePath))
                return;

            if (!environmentsManager.TryDuplicateActiveEnvironment(saveFilePath, out var newEnvironmentIndex))
                return;

            environmentsManager.SelectEnvironmentAtIndex(newEnvironmentIndex);
            environmentsManager.OpenActiveEnvironmentForEditing();
        }

        static void OnEditEnvironmentSelected()
        {
            SimulationEnvironmentAssetsManager.Instance.OpenActiveEnvironmentForEditing();
        }
    }
}