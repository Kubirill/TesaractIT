﻿//========= Copyright 2016-2020, HTC Corporation. All rights reserved. ===========

using HTC.UnityPlugin.ColliderEvent;
using HTC.UnityPlugin.Utility;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using GrabberPool = HTC.UnityPlugin.Utility.ObjectPool<HTC.UnityPlugin.Vive.BasicGrabbable.Grabber>;

namespace HTC.UnityPlugin.Vive
{
    [AddComponentMenu("VIU/Object Grabber/Basic Grabbable", 0)]
    public class BasicGrabbable : GrabbableBase<BasicGrabbable.Grabber>
        , IColliderEventDragStartHandler
        , IColliderEventDragFixedUpdateHandler
        , IColliderEventDragUpdateHandler
        , IColliderEventDragEndHandler
    {
        [Serializable]
        public class UnityEventGrabbable : UnityEvent<BasicGrabbable> { }

        public class Grabber : IGrabber
        {
            private static GrabberPool m_pool;

            public static Grabber Get(ColliderButtonEventData eventData)
            {
                if (m_pool == null)
                {
                    m_pool = new GrabberPool(() => new Grabber());
                }

                var grabber = m_pool.Get();
                grabber.eventData = eventData;
                return grabber;
            }

            public static void Release(Grabber grabber)
            {
                grabber.eventData = null;
                m_pool.Release(grabber);
            }

            public ColliderButtonEventData eventData { get; private set; }

            public RigidPose grabberOrigin
            {
                get
                {
                    return new RigidPose(eventData.eventCaster.transform);
                }
            }

            public RigidPose grabOffset { get; set; }
        }

        private IndexedTable<ColliderButtonEventData, Grabber> m_eventGrabberSet;

        public bool alignPosition;
        public bool alignRotation;
        public Vector3 alignPositionOffset;
        public Vector3 alignRotationOffset;

        [Range(MIN_FOLLOWING_DURATION, MAX_FOLLOWING_DURATION)]
        [FormerlySerializedAs("followingDuration")]
        [SerializeField]
        private float m_followingDuration = DEFAULT_FOLLOWING_DURATION;
        [FormerlySerializedAs("overrideMaxAngularVelocity")]
        [SerializeField]
        private bool m_overrideMaxAngularVelocity = true;
        [FormerlySerializedAs("unblockableGrab")]
        [SerializeField]
        private bool m_unblockableGrab = true;
        [SerializeField]
        [FlagsFromEnum(typeof(ControllerButton))]
        private ulong m_primaryGrabButton = 0ul;
        [SerializeField]
        [FlagsFromEnum(typeof(ColliderButtonEventData.InputButton))]
        private uint m_secondaryGrabButton = 1u << (int)ColliderButtonEventData.InputButton.Trigger;
        [SerializeField]
        [HideInInspector]
        private ColliderButtonEventData.InputButton m_grabButton = ColliderButtonEventData.InputButton.Trigger;
        [SerializeField]
        private bool m_allowMultipleGrabbers = true;
        [FormerlySerializedAs("afterGrabbed")]
        [SerializeField]
        private UnityEventGrabbable m_afterGrabbed = new UnityEventGrabbable();
        [FormerlySerializedAs("beforeRelease")]
        [SerializeField]
        private UnityEventGrabbable m_beforeRelease = new UnityEventGrabbable();
        [FormerlySerializedAs("onDrop")]
        [SerializeField]
        private UnityEventGrabbable m_onDrop = new UnityEventGrabbable(); // change rigidbody drop velocity here

        public override float followingDuration { get { return m_followingDuration; } set { m_followingDuration = Mathf.Clamp(value, MIN_FOLLOWING_DURATION, MAX_FOLLOWING_DURATION); } }

        public override bool overrideMaxAngularVelocity { get { return m_overrideMaxAngularVelocity; } set { m_overrideMaxAngularVelocity = value; } }

        public bool unblockableGrab { get { return m_unblockableGrab; } set { m_unblockableGrab = value; } }

        public UnityEventGrabbable afterGrabbed { get { return m_afterGrabbed; } }

        public UnityEventGrabbable beforeRelease { get { return m_beforeRelease; } }

        public UnityEventGrabbable onDrop { get { return m_onDrop; } }

        public ColliderButtonEventData grabbedEvent { get { return isGrabbed ? currentGrabber.eventData : null; } }

        public ulong primaryGrabButton { get { return m_primaryGrabButton; } set { m_primaryGrabButton = value; } }

        public uint secondaryGrabButton { get { return m_secondaryGrabButton; } set { m_secondaryGrabButton = value; } }

        public bool isTeleport = false;

        //public Transform newTransform;
        public Vector3 pos;
        public Quaternion rot;

        [Obsolete("Use IsSecondaryGrabButtonOn and SetSecondaryGrabButton instead")]
        public ColliderButtonEventData.InputButton grabButton
        {
            get
            {
                for (uint btn = 0u, btns = m_secondaryGrabButton; btns > 0u; btns >>= 1, ++btn)
                {
                    if ((btns & 1u) > 0u) { return (ColliderButtonEventData.InputButton)btn; }
                }
                return ColliderButtonEventData.InputButton.None;
            }
            set { m_secondaryGrabButton = 1u << (int)value; }
        }

        private bool moveByVelocity { get { return !unblockableGrab && grabRigidbody != null && !grabRigidbody.isKinematic; } }

        public bool IsPrimeryGrabButtonOn(ControllerButton btn) { return EnumUtils.GetFlag(m_primaryGrabButton, (int)btn); }

        public void SetPrimeryGrabButton(ControllerButton btn, bool isOn = true) { EnumUtils.SetFlag(ref m_primaryGrabButton, (int)btn, isOn); }

        public void ClearPrimeryGrabButton() { m_primaryGrabButton = 0ul; }

        public bool IsSecondaryGrabButtonOn(ColliderButtonEventData.InputButton btn) { return EnumUtils.GetFlag(m_secondaryGrabButton, (int)btn); }

        public void SetSecondaryGrabButton(ColliderButtonEventData.InputButton btn, bool isOn = true) { EnumUtils.SetFlag(ref m_secondaryGrabButton, (int)btn, isOn); }

        public void ClearSecondaryGrabButton() { m_secondaryGrabButton = 0u; }

        [Obsolete("Use grabRigidbody instead")]
        public Rigidbody rigid { get { return grabRigidbody; } set { grabRigidbody = value; } }

#if UNITY_EDITOR
        protected virtual void OnValidate() { RestoreObsoleteGrabButton(); }
#endif
        private void RestoreObsoleteGrabButton()
        {
            if (m_grabButton == ColliderButtonEventData.InputButton.Trigger) { return; }
            ClearSecondaryGrabButton();
            SetSecondaryGrabButton(m_grabButton, true);
            m_grabButton = ColliderButtonEventData.InputButton.Trigger;
        }

        protected override void Awake()
        {
            base.Awake();

            RestoreObsoleteGrabButton();

            afterGrabberGrabbed += () => m_afterGrabbed.Invoke(this);
            beforeGrabberReleased += () => m_beforeRelease.Invoke(this);
            onGrabberDrop += () => m_onDrop.Invoke(this);
        }

        protected virtual void OnDisable()
        {
            ClearGrabbers(true);
            ClearEventGrabberSet();
        }

        private void ClearEventGrabberSet()
        {
            if (m_eventGrabberSet == null) { return; }

            for (int i = m_eventGrabberSet.Count - 1; i >= 0; --i)
            {
                Grabber.Release(m_eventGrabberSet.GetValueByIndex(i));
            }

            m_eventGrabberSet.Clear();
        }

        protected bool IsValidGrabButton(ColliderButtonEventData eventData)
        {
            if (m_primaryGrabButton > 0ul)
            {
                ViveColliderButtonEventData viveEventData;
                if (eventData.TryGetViveButtonEventData(out viveEventData) && IsPrimeryGrabButtonOn(viveEventData.viveButton)) { return true; }
            }

            return m_secondaryGrabButton > 0u && IsSecondaryGrabButtonOn(eventData.button);
        }

        public virtual void OnColliderEventDragStart(ColliderButtonEventData eventData)
        {
            if (!IsValidGrabButton(eventData)) { return; }

            if (!m_allowMultipleGrabbers)
            {
                ClearGrabbers(false);
                ClearEventGrabberSet();
            }

            var grabber = Grabber.Get(eventData);
            var offset = RigidPose.FromToPose(grabber.grabberOrigin, new RigidPose(transform));
            if (alignPosition) { offset.pos = alignPositionOffset; }
            if (alignRotation) { offset.rot = Quaternion.Euler(alignRotationOffset); }
            grabber.grabOffset = offset;

            if (m_eventGrabberSet == null) { m_eventGrabberSet = new IndexedTable<ColliderButtonEventData, Grabber>(); }
            m_eventGrabberSet.Add(eventData, grabber);

            AddGrabber(grabber);
            //if (gameObject.GetComponent<PortalTraveller>() != null) gameObject.GetComponent<PortalTraveller>().readyToTeleport = false;
        }
       

        public virtual void OnColliderEventDragFixedUpdate(ColliderButtonEventData eventData)
        {
           
            if (isGrabbed && moveByVelocity && currentGrabber.eventData == eventData)
            {
                if (isTeleport)
                {
                    isTeleport = false;

                    OnGrabRigidbody(new RigidPose(pos, rot));
                }
                else OnGrabRigidbody();
            }
            else
            {
                if (isTeleport) isTeleport = false;
            }
        }

        public virtual void OnColliderEventDragUpdate(ColliderButtonEventData eventData)
        {
            
            if (isGrabbed && !moveByVelocity && currentGrabber.eventData == eventData)
            {
                RecordLatestPosesForDrop(Time.time, 0.05f);
                if (isTeleport)
                {
                    isTeleport = false;
                    

                    OnGrabTransform(new RigidPose(pos, rot));
                }
                else OnGrabTransform();
            }
            else
            {
                if (isTeleport) isTeleport = false;
            }

        }

        public virtual void OnColliderEventDragEnd(ColliderButtonEventData eventData)
        {
            //if (gameObject.GetComponent<PortalTraveller>() != null) gameObject.GetComponent<PortalTraveller>().readyToTeleport = true;

            if (m_eventGrabberSet == null) { return; }

            Grabber grabber;
            if (!m_eventGrabberSet.TryGetValue(eventData, out grabber)) { return; }

            RemoveGrabber(grabber);
            m_eventGrabberSet.Remove(eventData);
            Grabber.Release(grabber);
        }
    }
}