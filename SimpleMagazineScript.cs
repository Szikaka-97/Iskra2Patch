using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Receiver2;
using SimpleJSON;

namespace Iskra2Patch {
	public class SimpleMagazineScript : StorableMonoBehaviour {
		
		private readonly int max_rounds = 6;
		private readonly float round_move = 0.0055f;

		public GameObject round_prefab;
		public int queue_rounds = 0;

		private bool extracting;
		private Stack<ShellCasingScript> rounds = new();
		public int num_rounds {
			get { return rounds.Count; }
			set {
				rounds.Clear();

				for (int i = 0; i < max_rounds && i < value; i++) {
					GameObject round = Instantiate(round_prefab);

					round.GetComponent<ShellCasingScript>().go_round.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

					round.transform.parent = transform;
					round.transform.localRotation = round_insert.localRotation;
					round.transform.localPosition = (i % 2 == 0 ? round_top_left.localPosition : round_top_right.localPosition) - (transform.up * round_move * i);

					rounds.Push(round.GetComponent<ShellCasingScript>());
                }

				if (value > 1) follower.transform.localPosition = Vector3.zero - (transform.forward * round_move * (value - 1));
			}
		}
		public InventorySlot slot;

		private Transform round_insert;
		private Transform round_top_left;
		private Transform round_top_right;
		private Transform round_chamber;

		private Transform follower;

		public float round_insert_amount = 1;
		public bool can_insert_round {
			get { return round_insert_amount == 1; }
		}

		void Awake() {
			this.slot = base.GetComponent<InventorySlot>();
			if (slot == null) slot = base.gameObject.AddComponent<InventorySlot>();

			slot.type = InventorySlot.Type.Magazine;

			round_insert = transform.Find("round_insert");
			round_top_left = transform.Find("round_top_left");
			round_top_right = transform.Find("round_top_right");
			round_chamber = transform.Find("round_chamber");

			follower = transform.Find("follower");

			num_rounds = queue_rounds;
			queue_rounds = 0;
		}

		void Update() {
			if (round_insert_amount != 1) {
				rounds.ElementAt(0).transform.localPosition = Vector3.Lerp(
					round_insert.localPosition,
					num_rounds % 2 == 0 ? round_top_right.localPosition : round_top_left.localPosition,
					round_insert_amount
				);

				for (int i = 1; i < num_rounds; i++) {
					Transform round_transform = rounds.ElementAt(i).transform;

					Vector3 pos = new Vector3(
						round_transform.localPosition.x,
						round_transform.localPosition.y,
						Mathf.LerpUnclamped(round_top_left.localPosition.z, round_top_left.localPosition.z - round_move, i + round_insert_amount - 1)
					);

					round_transform.localPosition = pos;
				}

				if (num_rounds > 1) follower.transform.localPosition = new Vector3(0, 0, Mathf.Min(follower.transform.localPosition.z, Mathf.LerpUnclamped(0, -round_move, num_rounds - 2 + round_insert_amount)));

				round_insert_amount = Mathf.MoveTowards(round_insert_amount, 1, Time.deltaTime * 7 * Time.timeScale);
			}

			if (num_rounds != 0 && round_insert_amount == 1) {
				float distance = rounds.ElementAt(0).transform.localPosition.z - round_top_left.localPosition.z;

				if (distance < 0){
					for (int i = 0; i < num_rounds; i++) {
						ShellCasingScript round = rounds.ElementAt(i);

						round.transform.localPosition = new Vector3(
							round.transform.localPosition.x,
							round.transform.localPosition.y,
							Mathf.MoveTowards(round.transform.localPosition.z, round_top_left.localPosition.z - (i * round_move), Time.deltaTime * Time.timeScale)
						);
                    }
				}

				follower.transform.localPosition = Vector3.zero - (Vector3.forward * round_move * (num_rounds - 1)) + (Vector3.forward * distance);
            }

			if (extracting) {
				GunScript gunScript = transform.parent.GetComponent<GunScript>();
				var properties = transform.parent.GetComponent<Iskra2WeaponProperties>();

				if (properties.bolt.transform.localPosition.z <= round_chamber.localPosition.y) {
					float ratio = Mathf.InverseLerp(round_top_left.localPosition.y, round_chamber.localPosition.y, properties.bolt.transform.localPosition.z);
					gunScript.round_in_chamber.transform.position = Vector3.Lerp(num_rounds % 2 == 0 ? round_top_left.position : round_top_right.position, round_chamber.position, ratio);
					gunScript.round_in_chamber.transform.rotation = Quaternion.Lerp(num_rounds % 2 == 0 ? round_top_left.rotation : round_top_right.rotation, round_chamber.rotation, ratio);
                }
				else if (properties.bolt.transform.localPosition.z < 0){
					float ratio = Mathf.InverseLerp(round_chamber.localPosition.y, 0, properties.bolt.transform.localPosition.z);
					gunScript.round_in_chamber.transform.position = Vector3.Lerp(round_chamber.position, gunScript.transform.position, ratio);
					gunScript.round_in_chamber.transform.rotation = Quaternion.Lerp(round_chamber.rotation, gunScript.transform.rotation, ratio);
                }
				else {
					gunScript.round_in_chamber.transform.parent = gunScript.transform;
					extracting = false;
				}
			}
		}

		public bool AddRound(ShellCasingScript round) {
			if (round == null || num_rounds >= max_rounds) return false;

			LocalAimHandler.player_instance.MoveInventoryItem(round, slot);
			round.transform.parent = transform;
			round.transform.localScale = Vector3.one;

			round.transform.localPosition = round_insert.localPosition;
			round.transform.localRotation = round_insert.localRotation;

			round_insert_amount = 0;

			rounds.Push(round);

			return true;
		}

		public ShellCasingScript RemoveRound() {
			if (num_rounds <= 0) return null;

			extracting = true;

			if (ConfigFiles.global.infinite_ammo) return Instantiate(
				round_prefab, 
				rounds.ElementAt(0).transform.position,
				rounds.ElementAt(0).transform.rotation
			).GetComponent<ShellCasingScript>();
			return rounds.Pop();
		}

		public override string TypeName() {
			return "magazine";
		}

		public override void SetPersistentData(JSONObject data) {
			queue_rounds = data["rounds_in_mag"];

			Debug.LogWarning("Custom Mag Data Loaded");
		}

		public override JSONObject GetPersistentData() {
			JSONObject data = new();
			data.Add("rounds_in_mag", num_rounds);
			data.Add("spring_quality", 1);

			Debug.LogWarning("Custom Mag Data Saved");

			return data;
		}

	}
}
