using UnityEngine;
using Receiver2;

namespace Iskra2Patch {
	public class ScopeScript : MonoBehaviour{
		
		private Camera camera;
		private Material render_material;
		// Scope shader by NAKAI https://shattereddeveloper.blogspot.com/2012/11/creating-colored-unlit-texture-shader.html
		private GameObject crosshair;
		// Got the crosshair from http://athlonoptics.com/product/rifle-scopes-neos-4-12x40-center-x-sfp/
		private GameObject zoom_indicator;
		private GameObject zoom_indicator_background;

		private float min_zoom = 4.5f;
		private float max_zoom = 1.7f;
		
		void Awake() {
			camera = transform.Find("camera").GetComponent<Camera>();

			render_material = transform.Find("tube/rear_lens").GetComponent<MeshRenderer>().material;
			crosshair = transform.Find("tube/crosshair").gameObject;
			zoom_indicator = transform.Find("tube/indicator").gameObject;
			zoom_indicator_background = transform.Find("tube/indicator_background").gameObject;
		}

		void Update() {
			Vector3 distance = LocalAimHandler.player_instance.main_camera.transform.position - crosshair.transform.position;
			distance.Normalize();

			float rel = Vector3.Dot(transform.parent.forward, -distance);

			render_material.color = Color.HSVToRGB(0, 0, Mathf.Max(rel - 0.995f, 0) * 200);

			if (rel <= 0.995) {
				camera.enabled = false;
				crosshair.SetActive(false);
				zoom_indicator.SetActive(false);
				zoom_indicator_background.SetActive(false);
				return;
			}

			camera.enabled = true;

			crosshair.SetActive(true);
			if (crosshair.activeSelf) {
				zoom_indicator_background.SetActive(Plugin.indicator_active.Value);
				if (zoom_indicator_background.activeSelf && ColorUtility.DoTryParseHtmlColor(Plugin.indicator_background_color.Value, out Color32 background_color)) {
					Color.RGBToHSV(background_color, out float H, out float S, out float V);
					zoom_indicator_background.GetComponent<SpriteRenderer>().color = Color.HSVToRGB(H, S, Mathf.Max(rel - 0.995f, 0) * (V * 200));
				}
			}

			zoom_indicator.SetActive(Plugin.indicator_active.Value);
			if (zoom_indicator.activeSelf && ColorUtility.DoTryParseHtmlColor(Plugin.indicator_color.Value, out Color32 needle_color)) {
				Color.RGBToHSV(needle_color, out float H, out float S, out float V);
				zoom_indicator.GetComponent<SpriteRenderer>().color = Color.HSVToRGB(H, S, Mathf.Max(rel - 0.995f, 0) * (V * 200));
			}

			LocalAimHandler lah = LocalAimHandler.player_instance;

			if (lah.character_input.GetAxis(36) > 0 && camera.fieldOfView != max_zoom) {
				camera.fieldOfView = Mathf.MoveTowards(camera.fieldOfView, max_zoom, 0.7f);
				AudioManager.PlayOneShotAttached(Plugin.soundEvents["event_change_magnification"], gameObject, 0.2f);
			}
			if (lah.character_input.GetAxis(36) < 0 && camera.fieldOfView != min_zoom) {
				camera.fieldOfView = Mathf.MoveTowards(camera.fieldOfView, min_zoom, 0.7f);
				AudioManager.PlayOneShotAttached(Plugin.soundEvents["event_change_magnification"], gameObject, 0.2f);
			}

			zoom_indicator.transform.localEulerAngles = new Vector3(90, 0, -20 + (Mathf.InverseLerp(min_zoom, max_zoom, camera.fieldOfView) * 40));
		}
	}
}
