using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

namespace UniStorm.Utility
{
    [InitializeOnLoad]
    public class UniStormDefines
    {
        const string UniStormDefinesString = "UNISTORM_PRESENT";

        static UniStormDefines()
        {
            InitializeUniStormDefines();
        }

        static void InitializeUniStormDefines()
        {
            var BTG = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            string UniStormDef = PlayerSettings.GetScriptingDefineSymbols(BTG);

            if (!UniStormDef.Contains(UniStormDefinesString))
            {
                if (string.IsNullOrEmpty(UniStormDef))
                {
                    PlayerSettings.SetScriptingDefineSymbols(BTG, UniStormDefinesString);
                }
                else
                {
                    if (UniStormDef[UniStormDef.Length - 1] != ';')
                    {
                        UniStormDef += ';';
                    }

                    UniStormDef += UniStormDefinesString;
                    PlayerSettings.SetScriptingDefineSymbols(BTG, UniStormDef);
                }
            }
        }
    }
}