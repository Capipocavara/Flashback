using System;
using UnityEditor;
using UnityEngine;

namespace Flashback.Editor
{
    [CustomEditor(typeof(FlashbackCamcorder))]
    public sealed class FlashbackCamcorderEditor : UnityEditor.Editor
    {
        // Dimensions
        private SerializedProperty width;
        private SerializedProperty height;
        private SerializedProperty aspectRatio;

        // Recording Format
        private SerializedProperty framesPerSecond;
        private SerializedProperty quality;
        private SerializedProperty repeat;
        private SerializedProperty frameBufferSize;
        private SerializedProperty longpressDelay;

        // File Format
        private SerializedProperty saveFolder;
        private SerializedProperty filePrefix;
        private SerializedProperty fileStyle;
        private SerializedProperty encodingPriority;
        private SerializedProperty autoStart;

        // Events
        private SerializedProperty onFileSaveProgress;
        private SerializedProperty onFileSaved;
        private SerializedProperty onStopped;

        // Filename examples
        private string fileNameExample = "";
        private bool showEvents;
        private FlashbackCamcorder inspectedFlashbackCamcorder;

        private void OnEnable()
        {
            // Dimensions
            width = serializedObject.FindProperty("width");
            height = serializedObject.FindProperty("height");
            aspectRatio = serializedObject.FindProperty("aspectRatio");
            // Recording Format
            framesPerSecond = serializedObject.FindProperty("framesPerSecond");
            quality = serializedObject.FindProperty("quality");
            repeat = serializedObject.FindProperty("repeat");
            frameBufferSize = serializedObject.FindProperty("frameBufferSize");
            longpressDelay = serializedObject.FindProperty("longpressDelay");
            // File Format
            saveFolder = serializedObject.FindProperty("currentSaveFolder");
            filePrefix = serializedObject.FindProperty("currentFilePrefix");
            fileStyle = serializedObject.FindProperty("currentFileStyle");
            encodingPriority = serializedObject.FindProperty("EncodingPriority");
            autoStart = serializedObject.FindProperty("AutoStart");
            // Events
            onFileSaveProgress = serializedObject.FindProperty("OnFileSaveProgress");
            onFileSaved = serializedObject.FindProperty("OnFileSaved");
            onStopped = serializedObject.FindProperty("OnStopped");

            inspectedFlashbackCamcorder = (FlashbackCamcorder)target;
            inspectedFlashbackCamcorder.GeneratePath();
            GenerateExample();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Statistics monitor and warning
            if (Application.isEditor && Application.isPlaying) {
                EditorGUILayout.HelpBox(inspectedFlashbackCamcorder.Statistics, MessageType.Info);
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("The Recording Dimensions and Format default values can not be tweaked at runtime. To change values at runtime, use the Setup() method.", MessageType.Warning);
                GUI.enabled = false;
            }

            // Dimensions
            EditorGUILayout.LabelField("Dimensions", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(width, new GUIContent("Width", "Output gif width in pixels."));
            if (aspectRatio.enumValueIndex == (int)CamcorderAspectRatio.Custom)
                EditorGUILayout.PropertyField(height, new GUIContent("Height", "Output gif height in pixels."));
            else
                EditorGUILayout.LabelField(new GUIContent("Height", "Output gif height in pixels."), new GUIContent(height.intValue.ToString()));


            EditorGUILayout.PropertyField(aspectRatio, new GUIContent("Aspect Ration", "The desired Aspect Ratio for automatic height calculation."));
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
                inspectedFlashbackCamcorder.ComputeHeight();
            }

            // Recording Format
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recording Format", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(framesPerSecond, new GUIContent("Frames Per Second", "The number of frames per second the gif will run at."));
            EditorGUILayout.PropertyField(quality, new GUIContent("Compression Quality", "Lower values mean better quality but slightly longer processing time. 15 is generally a good middleground value."));
            EditorGUILayout.PropertyField(repeat, new GUIContent("Repeat", "-1 to disable, 0 to loop indefinitely, >0 to loop a set number of time."));
            EditorGUILayout.PropertyField(frameBufferSize, new GUIContent("Record Time", "The amount of time (in seconds) to record to memory."));
            EditorGUILayout.PropertyField(longpressDelay, new GUIContent("Longpress Delay", "Longpress capture support. Capture additional frames (in seconds) to start gif creation from the start of a longpress."));
            if (Application.isEditor && Application.isPlaying) {
                GUI.enabled = true;
            }

            // File Format
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("File Format", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(saveFolder, new GUIContent("Save Folder", "The folder to save the gifs to. No trailing slash."));

            // Update Example File Name
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(filePrefix, new GUIContent("File Prefix", "The file prefix for saved the gifs."));
            EditorGUILayout.PropertyField(fileStyle, new GUIContent("File Style", "Add a Count or a Timestamp to saved gifs."));
            if (EditorGUI.EndChangeCheck()) {
                serializedObject.ApplyModifiedProperties();
                GenerateExample();
            }

            EditorGUILayout.HelpBox("Example: \"" + fileNameExample + "\"" + Environment.NewLine + "Estimated VRam Usage: " + inspectedFlashbackCamcorder.EstimatedMemoryUse.ToString("F3") + " MB", MessageType.Info);
            EditorGUILayout.PropertyField(encodingPriority, new GUIContent("Encoder Priority", "Thread priority to use when processing frames to a gif file."));
            EditorGUILayout.PropertyField(autoStart, new GUIContent("Auto Start", "Automatically start recording on camcorder initialization."));

            showEvents = EditorGUILayout.Foldout(showEvents, new GUIContent("File Saving Events", "Events related to encoding process and file saving."), true);
            if (showEvents) {
                EditorGUILayout.PropertyField(onFileSaveProgress, new GUIContent("Save Progress", "Invoked during gif encoding. Returns ID and Progress (0f - 1f)."));
                EditorGUILayout.PropertyField(onFileSaved, new GUIContent("File Saved", "Invoked everytime a gif is saved. Returns ID and Filename."));
                EditorGUILayout.PropertyField(onStopped, new GUIContent("Stopped", "Invoked when the camcorder is finished suspending after calling Stop()"));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void GenerateExample()
        {
            switch (fileStyle.enumValueIndex) {
                case (int)CamcorderFileStyle.Timestamp:
                    fileNameExample = filePrefix.stringValue + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + "'" + DateTime.Now.Millisecond.ToString("D4") + ".gif";
                    break;
                case (int)CamcorderFileStyle.Numbered:
                    System.Random rand = new System.Random();
                    fileNameExample = filePrefix.stringValue + " " + rand.Next(100).ToString("D4") + ".gif";
                    break;
            }
        }
    }
}