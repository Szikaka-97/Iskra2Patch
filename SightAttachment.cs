using UnityEngine;
using Receiver2;

namespace Iskra2Patch {
	public class SightAttachment {
		public string name;
		private Transform sight_transform;
		public Transform ads_pose {
			get;
		}
		public Transform point_bullet_fire {
			get;
        }

		public SightAttachment(string name, Transform sight_transform, Transform ads_pose, Transform point_bullet_fire) {
			this.name = name;
			this.sight_transform = sight_transform;
			this.ads_pose = ads_pose;
			this.point_bullet_fire = point_bullet_fire;
		}

		public void Enable(GunScript script) {
			sight_transform.gameObject.SetActive(true);

			script.transform.Find("pose_aim_down_sights").localPosition = ads_pose.localPosition;
			script.transform_bullet_fire = point_bullet_fire;

			script.GetComponent<Iskra2WeaponProperties>().currentSight = this;
		}
		public void Disable() {
			sight_transform.gameObject.SetActive(false);
		}
	}
}
