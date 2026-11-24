using UnityEditor;
using UnityEngine;

namespace CpcPrototype.Editor
{
    [CustomEditor(typeof(SurvivalZombie)), CanEditMultipleObjects]
    public sealed class SurvivalZombieEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Kind"));
            bool kindChanged = EditorGUI.EndChangeCheck();
            DrawPropertiesExcluding(serializedObject, "m_Script", "Kind");
            serializedObject.ApplyModifiedProperties();
            if (kindChanged)
            {
                foreach (Object item in targets)
                {
                    SurvivalZombie zombie = (SurvivalZombie)item;
                    zombie.RefreshAbility();
                    EditorUtility.SetDirty(zombie);
                }
            }
            EditorGUILayout.HelpBox("종류를 선택하면 해당 스킬 컴포넌트가 자동으로 붙습니다. 같은 종류 전체에 적용하려면 CombatPrefabs의 해당 프리팹을 수정하세요.", MessageType.Info);
        }
    }
}
