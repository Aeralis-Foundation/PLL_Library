using System;
using UnityEditor;

namespace PLL.Creator.Editor
{
    internal static class PllCreatorProjectLayers
    {
        const string TagManagerAssetPath = "ProjectSettings/TagManager.asset";

        internal static bool TryInspect(
            PllCreatorContractLayer[] RequiredLayers,
            out PllCreatorPendingLayer[] ProjectLayers, out string Failure)
        {
            SerializedObject TagManager;
            SerializedProperty Layers;
            if (!TryOpen(out TagManager, out Layers, out Failure))
            {
                ProjectLayers = null;
                return false;
            }
            ProjectLayers = CreateProjectLayers(RequiredLayers);
            if (!NamesUseExpectedIndexes(Layers, ProjectLayers, out Failure))
            {
                ProjectLayers = null;
                return false;
            }
            for (var Index = 0; Index < ProjectLayers.Length; Index++)
            {
                var Required = ProjectLayers[Index];
                if (Required.Index >= Layers.arraySize)
                {
                    ProjectLayers = null;
                    Failure = "ProjectSettings/TagManager.asset has no layer " + Required.Index + ".";
                    return false;
                }
                var CurrentName = Layers.GetArrayElementAtIndex(Required.Index).stringValue;
                if (!string.IsNullOrEmpty(CurrentName)
                    && !ExactNameIndex(CurrentName, Required.Index, Required))
                {
                    ProjectLayers = null;
                    Failure = "Project layer " + Required.Index + " is '" + CurrentName
                        + "'. Set it to '" + Required.Name + "' or clear it before configuring PLL Creator.";
                    return false;
                }
                Required.PreviouslyPresent = ExactNameIndex(CurrentName, Required.Index, Required);
            }
            Failure = string.Empty;
            return true;
        }

        internal static bool MatchesContract(
            PllCreatorPendingLayer[] ProjectLayers, PllCreatorContractLayer[] RequiredLayers)
        {
            if (ProjectLayers == null || RequiredLayers == null
                || ProjectLayers.Length != RequiredLayers.Length) return false;
            foreach (var Required in RequiredLayers)
            {
                var Found = false;
                foreach (var Layer in ProjectLayers)
                    if (Layer != null && Layer.Index == Required.index
                        && string.Equals(Layer.Name, Required.name, StringComparison.Ordinal))
                    {
                        Found = true;
                        break;
                    }
                if (!Found) return false;
            }
            return true;
        }

        internal static void Install(
            PllCreatorPendingLayer[] ProjectLayers, ref bool MutationStarted)
        {
            SerializedObject TagManager;
            SerializedProperty Layers;
            string Failure;
            if (!TryOpen(out TagManager, out Layers, out Failure))
                throw new PllCreatorSetupException(Failure);
            if (!NamesUseExpectedIndexes(Layers, ProjectLayers, out Failure))
                throw new PllCreatorSetupException(Failure);
            if (!HasOwnedChanges(ProjectLayers)) return;
            var Changed = false;
            foreach (var Layer in ProjectLayers)
            {
                if (Layer.PreviouslyPresent) continue;
                var Slot = Layers.GetArrayElementAtIndex(Layer.Index);
                if (ExactNameIndex(Slot.stringValue, Layer.Index, Layer)) continue;
                if (!string.IsNullOrEmpty(Slot.stringValue))
                    throw new PllCreatorSetupException("Project layer " + Layer.Index
                        + " changed before PLL Creator could install '" + Layer.Name + "'.");
                Slot.stringValue = Layer.Name;
                Changed = true;
            }
            if (!Changed) return;
            MutationStarted = true;
            if (!TagManager.ApplyModifiedPropertiesWithoutUndo())
                throw new PllCreatorSetupException("Unity did not apply the required project layers.");
            AssetDatabase.SaveAssetIfDirty(TagManager.targetObject);
            if (!Installed(ProjectLayers, out Failure)) throw new PllCreatorSetupException(Failure);
        }

        internal static void Restore(PllCreatorPendingLayer[] ProjectLayers)
        {
            SerializedObject TagManager;
            SerializedProperty Layers;
            string Failure;
            if (!TryOpen(out TagManager, out Layers, out Failure))
                throw new PllCreatorSetupException(Failure);
            if (!NamesUseExpectedIndexes(Layers, ProjectLayers, out Failure))
                throw new PllCreatorSetupException(Failure);
            if (!HasOwnedChanges(ProjectLayers)) return;
            var Changed = false;
            foreach (var Layer in ProjectLayers)
            {
                if (Layer.PreviouslyPresent) continue;
                var Slot = Layers.GetArrayElementAtIndex(Layer.Index);
                if (string.IsNullOrEmpty(Slot.stringValue)) continue;
                if (!ExactNameIndex(Slot.stringValue, Layer.Index, Layer))
                    throw new PllCreatorSetupException("Rollback stopped because project layer " + Layer.Index
                        + " no longer contains '" + Layer.Name + "'.");
                Slot.stringValue = string.Empty;
                Changed = true;
            }
            if (Changed)
            {
                if (!TagManager.ApplyModifiedPropertiesWithoutUndo())
                    throw new PllCreatorSetupException("Unity did not restore the project layers.");
                AssetDatabase.SaveAssetIfDirty(TagManager.targetObject);
            }
            if (!Restored(ProjectLayers, out Failure)) throw new PllCreatorSetupException(Failure);
        }

        internal static bool Installed(PllCreatorPendingLayer[] ProjectLayers, out string Failure)
        {
            SerializedObject TagManager;
            SerializedProperty Layers;
            if (!TryOpen(out TagManager, out Layers, out Failure)) return false;
            if (!NamesUseExpectedIndexes(Layers, ProjectLayers, out Failure)) return false;
            foreach (var Layer in ProjectLayers)
            {
                if (Layer.Index < Layers.arraySize && ExactNameIndex(
                        Layers.GetArrayElementAtIndex(Layer.Index).stringValue, Layer.Index, Layer)) continue;
                Failure = "Project layer " + Layer.Index + " must contain '" + Layer.Name + "'.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        internal static bool Installed(PllCreatorContractLayer[] RequiredLayers, out string Failure)
        {
            return Installed(CreateProjectLayers(RequiredLayers), out Failure);
        }

        internal static bool Restored(PllCreatorPendingLayer[] ProjectLayers, out string Failure)
        {
            SerializedObject TagManager;
            SerializedProperty Layers;
            if (!TryOpen(out TagManager, out Layers, out Failure)) return false;
            if (!NamesUseExpectedIndexes(Layers, ProjectLayers, out Failure)) return false;
            foreach (var Layer in ProjectLayers)
            {
                var CurrentName = Layer.Index < Layers.arraySize
                    ? Layers.GetArrayElementAtIndex(Layer.Index).stringValue : string.Empty;
                if (Layer.PreviouslyPresent
                    ? ExactNameIndex(CurrentName, Layer.Index, Layer) : string.IsNullOrEmpty(CurrentName)) continue;
                Failure = Layer.PreviouslyPresent
                    ? "Project layer " + Layer.Index + " no longer contains '" + Layer.Name + "'."
                    : "Project layer " + Layer.Index + " was not restored to its empty value.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }

        static bool HasOwnedChanges(PllCreatorPendingLayer[] ProjectLayers)
        {
            foreach (var Layer in ProjectLayers)
                if (!Layer.PreviouslyPresent) return true;
            return false;
        }

        static PllCreatorPendingLayer[] CreateProjectLayers(PllCreatorContractLayer[] RequiredLayers)
        {
            var ProjectLayers = new PllCreatorPendingLayer[RequiredLayers.Length];
            for (var Index = 0; Index < RequiredLayers.Length; Index++)
                ProjectLayers[Index] = new PllCreatorPendingLayer
                {
                    Index = RequiredLayers[Index].index,
                    Name = RequiredLayers[Index].name
                };
            return ProjectLayers;
        }

        static bool NamesUseExpectedIndexes(
            SerializedProperty Layers, PllCreatorPendingLayer[] ProjectLayers, out string Failure)
        {
            for (var Index = 0; Index < Layers.arraySize; Index++)
            {
                var CurrentName = Layers.GetArrayElementAtIndex(Index).stringValue;
                if (string.IsNullOrEmpty(CurrentName)) continue;
                foreach (var Layer in ProjectLayers)
                {
                    if (ExactNameIndex(CurrentName, Index, Layer)) break;
                    if (!string.Equals(CurrentName, Layer.Name, StringComparison.Ordinal)) continue;
                    Failure = "Project layer '" + CurrentName + "' is assigned to " + Index
                        + "; it must use " + Layer.Index + ".";
                    return false;
                }
            }
            Failure = string.Empty;
            return true;
        }

        static bool ExactNameIndex(string Name, int Index, PllCreatorPendingLayer Required)
        {
            return Index == Required.Index && string.Equals(Name, Required.Name, StringComparison.Ordinal);
        }

        static bool TryOpen(
            out SerializedObject TagManager, out SerializedProperty Layers, out string Failure)
        {
            var Assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerAssetPath);
            if (Assets.Length == 0)
            {
                TagManager = null;
                Layers = null;
                Failure = "Unity could not open ProjectSettings/TagManager.asset.";
                return false;
            }
            TagManager = new SerializedObject(Assets[0]);
            TagManager.Update();
            Layers = TagManager.FindProperty("layers");
            if (Layers == null || !Layers.isArray)
            {
                TagManager = null;
                Failure = "ProjectSettings/TagManager.asset has no layers array.";
                return false;
            }
            Failure = string.Empty;
            return true;
        }
    }
}