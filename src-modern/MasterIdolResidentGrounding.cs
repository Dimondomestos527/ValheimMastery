using UnityEngine;
namespace ValheimMastery
{
    // Read the native contact witness. No physics casts, root writes or gravity changes.
    internal static class MasterIdolResidentGrounding
    {
        private static bool Finite(Vector3 value)=>float.IsFinite(value.x)&&float.IsFinite(value.y)&&float.IsFinite(value.z);
        internal static bool Slow(Character actor)
        {
            if(actor==null||actor.IsDead()||actor.m_nview?.IsValid()!=true||!actor.IsOnGround())return false;
            var support=actor.GetLastGroundCollider();var point=actor.m_lastGroundPoint;var normal=actor.GetLastGroundNormal();
            if(support==null||!support.enabled||!support.gameObject.activeInHierarchy||support.isTrigger||!Finite(point)||!Finite(normal)||normal.y<.7f)return false;
            Vector3 feet=actor.transform.position,delta=feet-point;float radius=actor.GetRadius()+.15f;
            if(!Finite(feet)||!float.IsFinite(radius)||radius<=0||delta.y<-.1f||delta.y>.35f||delta.x*delta.x+delta.z*delta.z>radius*radius||support.bounds.SqrDistance(point)>.01f)return false;
            var velocity=actor.GetVelocity();var body=support.attachedRigidbody;
            if(body!=null)velocity-=body.GetPointVelocity(point);
            return Finite(velocity)&&velocity.sqrMagnitude<=.04f&&Mathf.Abs(velocity.y)<=.05f;
        }
    }
}
