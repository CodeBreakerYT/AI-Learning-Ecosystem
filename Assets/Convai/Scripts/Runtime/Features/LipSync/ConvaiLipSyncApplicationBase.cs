using System.Collections.Generic;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features.LipSync;
using Convai.Scripts.Runtime.Features.LipSync.Models;
using Service;
using UnityEngine;

namespace Convai.Scripts.Runtime.Features
{
    /// <summary>
    ///     This Class will serve as a base for any method of Lipsync that Convai will develop or use
    /// </summary>
    public abstract class ConvaiLipSyncApplicationBase : MonoBehaviour
    {
        /// <summary>
        ///     Reference to the NPC on which lipsync will be applied
        /// </summary>
        protected ConvaiNPC ConvaiNPC;

        /// <summary>
        ///     Cached Reference of Facial Expression Data
        /// </summary>
        protected FacialExpressionData FacialExpressionData;

        /// <summary>
        ///     Cached Reference of WeightBlendingPower
        /// </summary>
        protected float WeightBlendingPower;

        /// <summary>
        ///     Initializes and setup up of the things necessary for lipsync to work
        /// </summary>
        /// <param name="convaiLipSync"></param>
        /// <param name="convaiNPC"></param>
        public virtual void Initialize(ConvaiLipSync convaiLipSync, ConvaiNPC convaiNPC)
        {
            FacialExpressionData = convaiLipSync.FacialExpressionData;
            WeightBlendingPower = convaiLipSync.WeightBlendingPower;
            // Also require a real mesh with blend shapes, not just a non-null Renderer
            // reference - a character can have the SkinnedMeshRenderer component
            // wired up with its sharedMesh left unassigned (a broken/missing asset
            // reference), which used to pass this check and then crash
            // SetBlendShapeWeight with "Array index out of bounds (size=0)" every
            // frame once lipsync tried to drive a blend shape index that doesn't
            // exist on an empty mesh.
            HasHeadSkinnedMeshRenderer = HasUsableBlendShapes(FacialExpressionData.Head.Renderer);
            HasTeethSkinnedMeshRenderer = HasUsableBlendShapes(FacialExpressionData.Teeth.Renderer);
            HasTongueSkinnedMeshRenderer = HasUsableBlendShapes(FacialExpressionData.Tongue.Renderer);
            HasJawBone = FacialExpressionData.JawBone != null;
            HasTongueBone = FacialExpressionData.TongueBone != null;
            ConvaiNPC = convaiNPC;
        }

        private static bool HasUsableBlendShapes(SkinnedMeshRenderer renderer)
        {
            return renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0;
        }

        /// <summary>
        ///     Updates the tongue bone rotation to the new rotation
        /// </summary>
        /// <param name="newRotation"></param>
        protected void UpdateTongueBoneRotation(Vector3 newRotation)
        {
            if (!HasTongueBone) return;
            FacialExpressionData.TongueBone.transform.localEulerAngles = newRotation;
        }

        /// <summary>
        ///     Updates the jaw bone rotation to the new rotation
        /// </summary>
        /// <param name="newRotation"></param>
        protected void UpdateJawBoneRotation(Vector3 newRotation)
        {
            if (!HasJawBone) return;
            FacialExpressionData.JawBone.transform.localEulerAngles = newRotation;
        }


        /// <summary>
        ///     This removes the excess frames in the queue
        /// </summary>
        public abstract void PurgeExcessBlendShapeFrames();

        /// <summary>
        ///     This resets the whole queue of the frames
        /// </summary>
        protected bool CanPurge<T>(Queue<T> queue)
        {
            // ? Should I hardcode the limiter for this check
            return queue.Count < 10;
        }

        public abstract void ClearQueue();

        /// <summary>
        ///     Adds blendshape frames in the queue
        /// </summary>
        /// <param name="blendshapeFrames"></param>
        public virtual void EnqueueQueue(Queue<ARKitBlendShapes> blendshapeFrames)
        {
        }

        /// <summary>
        ///     Adds Visemes frames in the list
        /// </summary>
        /// <param name="visemesFrames"></param>
        public virtual void EnqueueQueue(Queue<VisemesData> visemesFrames)
        {
        }

        /// <summary>
        ///     Adds a blendshape frame in the last queue
        /// </summary>
        /// <param name="blendshapeFrame"></param>
        public virtual void EnqueueFrame(ARKitBlendShapes blendshapeFrame)
        {
        }

        /// <summary>
        ///     Adds a viseme frame to the last element of the list
        /// </summary>
        /// <param name="viseme"></param>
        public virtual void EnqueueFrame(VisemesData viseme)
        {
        }

        #region Null States of References

        protected bool HasHeadSkinnedMeshRenderer { get; private set; }
        protected bool HasTeethSkinnedMeshRenderer { get; private set; }
        protected bool HasTongueSkinnedMeshRenderer { get; private set; }
        private bool HasJawBone { get; set; }
        private bool HasTongueBone { get; set; }

        #endregion
    }
}