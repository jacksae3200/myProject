"""Build CPC train/eval JSONL using a local Ollama teacher."""

import argparse
import json
from pathlib import Path
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parent
TEACHER = "gemma4:e4b-it-q4_K_M"
ACTIONS = "none,follow,hold,guard,taunt,attack,retreat,intercept,evade,counterattack,protectplayer"
SYSTEM = "너는 Unity 게임의 한국어 동료 CPC다. 허용되고 실행 가능한 행동을 고르고 짧게 답한다."
ENEMIES = {
    "spit": "zombie_1 type=Spitter phase=Aiming target=player feasible=guard,attack,intercept,",
    "crawl": "zombie_2 type=Crawler phase=Recovering target=CPC feasible=guard,attack,counterattack,taunt,",
    "ground": "zombie_3 type=Burrower phase=Emerging target=player feasible=evade,",
    "normal": "zombie_4 type=Normal phase=Idle target=none feasible=guard,attack,taunt,",
}
OUTPUT_RULES = (
    'JSON 객체 하나만 출력한다. 필수 문자열 필드는 action, targetId, intent, speech다.',
    'intent는 command, question, clarify 중 하나다. follow, hold, attack 등은 intent가 아니라 action이다.',
    '실행 가능한 행동 요청은 command이며 action은 허용 목록에서 고른다.',
    '상태나 상황을 묻는 질문은 question이다. 현재 상태를 설명하고 기존 지시를 유지한다.',
    '대상이 관측되지 않는 공격 요청 등 확인이 필요한 경우 clarify다. 상황을 짧게 설명하고 확인한다.',
    'question과 clarify는 action=none, targetId=""이다. 기존 지시를 유지한다.',
    'hold와 follow 등 대상 없는 행동은 targetId=""이다. null이나 player를 쓰지 않는다.',
    'follow는 followMode를 포함한다. 교전 금지를 명시한 경우 passive, 그 외에는 protect다.',
    'speech에는 플레이어의 말을 복사하지 말고 CPC가 직접 말하는 짧은 한국어 대사를 쓴다.',
)
RULE_ANCHOR = "입력 종류=플레이어 지시/질문"

# Near-neighbor examples share the same game state but require different intents.
CONTRAST = [
    ("follow", (), "hold", "", "command", {}, "지금 여기서 기다려.", "응, 지금 있는 자리에서 기다릴게."),
    ("follow", (), "none", "", "question", {}, "지금 여기서 기다리는 중이야?", "지금은 네 뒤를 따라가는 중이야."),
    ("hold", (), "follow", "", "command", {"followMode": "protect"}, "지금부터 나 따라와.", "이제 네 뒤를 따라갈게."),
    ("hold", (), "none", "", "question", {}, "지금 나 따라오는 중이야?", "지금은 네 지시대로 여기서 대기 중이야."),
    ("hold", ("spit",), "none", "", "question", {}, "저 침을 막고 있는 중이야?", "지금은 침을 막고 있지 않아. 여기서 대기 중이야."),
    ("hold", ("spit",), "intercept", "zombie_1", "command", {}, "대기는 끝이야. 지금 저 침을 막아 줘.", "대기를 끝내고 널 겨눈 침을 막을게."),
    ("follow", ("normal",), "attack", "zombie_4", "command", {}, "저 좀비 공격해 줘.", "눈앞에 있는 좀비를 공격할게."),
    ("follow", (), "none", "", "clarify", {"goal": "combat"}, "저 좀비 공격해 줘.", "지금은 공격할 좀비가 보이지 않아. 어느 쪽이야?"),
    ("follow", ("crawl",), "none", "", "question", {}, "크롤러한테 반격할 수 있어?", "응, 공격을 마치고 회복 중이라 빈틈이 있어."),
    ("follow", ("crawl",), "counterattack", "zombie_2", "command", {}, "크롤러한테 지금 반격해 줘.", "크롤러가 회복하는 틈에 반격할게."),
    ("hold", (), "follow", "", "command", {"followMode": "passive"}, "교전하지 말고 지금 내 옆으로 와.", "싸우지 않고 네 옆으로 갈게."),
    ("hold", (), "follow", "", "command", {"followMode": "protect"}, "이제 내 옆으로 와. 주변도 살펴 줘.", "네 옆으로 가면서 주변을 살필게."),
]

FINAL_EVAL = [
    ("hold", ("ground",), "none", "", "question", {}, "바닥이 흔들리는데, 내 대기 지시는 아직 유지 중이야?", "응, 지금은 네 지시대로 대기 중이야."),
    ("follow", ("spit",), "hold", "", "command", {}, "침 공격은 내가 대응할게. 넌 거기서 멈춰 있어.", "알겠어. 여기서 멈춰 있을게."),
    ("hold", ("spit", "crawl"), "follow", "", "command", {"followMode": "passive"}, "대기 풀고 내 뒤에 붙어. 어느 쪽도 건드리지 마.", "싸우지 않고 네 뒤를 따라갈게."),
    ("hold", ("normal", "ground"), "follow", "", "command", {"followMode": "protect"}, "대기 끝. 내 곁으로 와서 주변을 경계해 줘.", "네 곁으로 가면서 주변을 경계할게."),
    ("follow", ("crawl", "spit"), "intercept", "zombie_1", "command", {}, "기어오는 건 됐고, 나한테 향하는 침부터 가로막아.", "너한테 향하는 침을 먼저 막을게."),
    ("follow", ("spit", "crawl"), "none", "", "question", {}, "회복하는 쪽은 기어오는 좀비 맞아?", "응, 기어오는 좀비가 공격을 끝내고 회복 중이야."),
    ("hold", (), "none", "", "clarify", {"goal": "combat"}, "저기 숨어 있는 좀비부터 처리해 줄래?", "지금은 처리할 좀비가 보이지 않아. 어느 쪽인지 알려 줘."),
    ("follow", ("ground", "crawl"), "evade", "zombie_3", "command", {}, "반격은 나중에 하고, 솟아오르는 버로워의 공격 범위부터 벗어나.", "버로워의 지면 공격 범위 밖으로 빠질게."),
]

# Each group has an independently labeled decision and several player phrasings.
TRAIN = [
    ("follow", (), "hold", "", "command", {}, ["여기서 기다려.", "잠깐 여기 있어 줘.", "내가 올 때까지 그 자리에 있어.", "일단 움직이지 마.", "나 먼저 보고 올게. 넌 여기 있어.", "잠시 대기해 줘."]),
    ("hold", (), "follow", "", "command", {"followMode": "protect"}, ["이제 따라와.", "기다리지 말고 따라와.", "대기 끝이야. 나랑 같이 가자.", "이제 내 뒤로 와.", "나랑 같이 움직여.", "다시 나를 따라와 줘."]),
    ("hold", (), "follow", "", "command", {"followMode": "passive"}, ["싸우지 말고 나만 따라와.", "적은 건드리지 말고 내 뒤로 와.", "교전은 하지 말고 같이 가자.", "공격하지 마. 그냥 따라와.", "싸움은 피하고 내 곁으로 와.", "좀비는 무시하고 나만 따라와 줘."]),
    ("hold", (), "none", "", "question", {}, ["지금 뭐 해?", "아직 여기서 기다리는 중이야?", "지금 하던 일이 뭐야?", "뭘 하고 있는지 말해 줘.", "지금도 대기하고 있어?", "너 지금 어떤 상태야?"]),
    ("follow", ("normal",), "attack", "zombie_4", "command", {"targetType": "Normal"}, ["일반 좀비부터 잡아.", "저 일반 좀비 처리해 줘.", "특수 좀비 말고 일반 좀비를 공격해.", "눈앞의 일반 좀비를 먼저 쓰러뜨려.", "저 평범한 좀비부터 잡아 줘.", "일반형을 우선 공격해."]),
    ("follow", ("spit",), "intercept", "zombie_1", "command", {}, ["저 침 좀 막아 줘.", "나한테 날아올 침을 가로막아.", "침 뱉는 놈이 날 겨눴어. 방패로 막아 줘.", "내가 침에 맞지 않게 가로막아.", "저 스피터가 쏠 침을 막아 줘.", "나한테 겨눈 침 공격을 대신 막아 줘."]),
    ("follow", ("ground",), "evade", "zombie_3", "command", {}, ["버로우 공격 범위에서 벗어나.", "땅에서 올라오는 공격을 피해.", "저 지면 공격 밖으로 빠져.", "버로워가 올라오는 자리에서 물러나.", "바닥 범위 공격이야. 범위 밖으로 피해.", "땅 밑에서 튀어나오는 놈의 공격을 피하자."]),
    ("follow", ("crawl",), "counterattack", "zombie_2", "command", {}, ["기어오는 놈이 빈틈을 보였어. 반격해.", "저 크롤러가 회복하는 동안 받아쳐.", "크롤러의 공격이 끝났어. 지금 반격해.", "기어오는 좀비가 자세를 추스르는 중이야. 반격해.", "저 크롤러의 빈틈에 반격해 줘.", "공격을 끝낸 크롤러에게 지금 받아쳐."]),
    ("follow", (), "none", "", "clarify", {"goal": "combat"}, ["저놈 공격해.", "저 좀비 잡아 줘.", "아까 그 적을 처리해.", "그 녀석을 공격해 줘.", "저기 있는 적부터 쓰러뜨려.", "내가 가리킨 좀비를 잡아."]),
    ("follow", ("spit", "normal"), "none", "", "question", {}, ["침 뱉는 좀비가 누구를 겨누고 있어?", "저 스피터의 표적이 나야?", "침 뱉는 놈이 지금 누구를 보고 있어?", "저 둘 중에 나를 조준하는 건 누구야?", "스피터가 나한테 침을 쏘려고 해?", "어떤 적이 나를 겨누고 있지?"]),
]

# Held-out combinations and human-authored reference replies.
EVAL = [
    ("hold", ("spit",), "none", "", "question", {}, "침 뱉는 놈이 나를 겨누는데 넌 지금 뭘 하고 있어?", "나는 네 지시대로 여기서 대기 중이야."),
    ("follow", ("crawl",), "hold", "", "command", {}, "기어오는 놈은 내가 맡을게. 넌 그 자리에서 대기해.", "알겠어. 지금 있는 자리에서 기다릴게."),
    ("hold", ("normal",), "follow", "", "command", {"followMode": "passive"}, "방금 대기하라고 한 건 취소야. 옆에 붙되 교전하지 마.", "싸우지 않고 네 곁으로 갈게."),
    ("hold", ("crawl",), "follow", "", "command", {"followMode": "protect"}, "대기는 풀고 나랑 같이 이동해 줘.", "알겠어. 이제 따라가며 주변을 살필게."),
    ("follow", ("normal", "spit"), "attack", "zombie_4", "command", {"targetType": "Normal"}, "침 뱉는 쪽은 내 몫이야. 넌 일반형을 쓰러뜨려.", "일반 좀비부터 처리할게."),
    ("follow", ("normal", "spit"), "intercept", "zombie_1", "command", {}, "일반 좀비는 놔두고 나를 겨눈 침을 대신 받아 줘.", "널 겨눈 침 공격을 가로막을게."),
    ("hold", ("normal", "ground"), "evade", "zombie_3", "command", {}, "대기 지시는 취소야. 버로워가 솟는 원 밖으로 빠져.", "버로워의 지면 공격 범위에서 벗어날게."),
    ("follow", ("spit", "crawl"), "counterattack", "zombie_2", "command", {}, "스피터 말고 공격을 마친 기어다니는 쪽에 반격해.", "빈틈을 보인 크롤러에게 반격할게."),
    ("hold", (), "none", "", "clarify", {"goal": "combat"}, "대기는 유지하고, 아까 보였던 적이 보이면 잡아 줄래?", "지금은 대상이 보이지 않아. 여기서 계속 대기할게."),
    ("follow", ("crawl",), "none", "", "question", {}, "크롤러가 아직 공격 중이야, 아니면 회복 중이야?", "지금은 공격을 마치고 회복 중이야."),
    ("follow", (), "none", "", "question", {}, "이 근처에 보이는 좀비가 있어?", "지금 관측 범위에는 좀비가 없어."),
    ("hold", ("normal",), "none", "", "question", {}, "지금 멈춰 있는 거야, 아니면 저 일반 좀비를 쫓는 중이야?", "지시대로 제자리에 멈춰 있어."),
]


def make_example(state, enemies, action, target, intent, extra, utterance):
    current = {"hold": "현재 위치에서 대기 중", "follow": "플레이어를 따라가는 중"}[state]
    prompt = [
        "Unity 전술 JSON: action,targetId,speech,intent. 선택 필드: followMode, targetType, targetPolicy, goal. speech는 한국어.",
        RULE_ANCHOR,
        *OUTPUT_RULES,
        f"플레이어 발언/사건: {utterance}",
        "질문은 question/none으로 기존 지시를 유지한다. 관측 대상이 없는 공격 명령은 clarify/none이다.",
        "대상 행동은 관측 ID 또는 auto를 사용한다. 각 적의 feasible 목록에 있는 행동만 선택한다.",
        "일반 따라와는 protect, 교전 금지를 명시한 추종은 passive다.",
        f"실제 CPC 행동: {current}",
        f"유지 중인 플레이어 지시: {state.capitalize()} target=",
        "추종 모드: protect(따라오며 주변 보호)",
        "성향: 플레이어 보호를 우선한다.",
        f"허용: {ACTIONS}",
        "player pos=(0,0,0) HP=100/100",
        "CPC pos=(1,0,0) HP=100/100",
        "priority=" + (ENEMIES[enemies[0]].split()[0] if enemies else ""),
        *(ENEMIES[enemy] for enemy in enemies),
        f"지금 답할 최신 플레이어 발언: {utterance}",
    ]
    messages = [{"role": "system", "content": SYSTEM}, {"role": "user", "content": "\n".join(prompt)}]
    decision = {"action": action, "targetId": target, "intent": intent, **extra}
    return messages, decision


def query(teacher, messages, decision):
    schema = {
        "type": "object",
        "properties": {
            **{key: {"type": "string", "enum": [value]} for key, value in decision.items()},
            "speech": {"type": "string"},
        },
        "required": [*decision, "speech"],
        "additionalProperties": False,
    }
    hint = json.dumps(decision, ensure_ascii=False)
    request = Request(
        "http://127.0.0.1:11434/api/chat",
        data=json.dumps({
            "model": teacher,
            "messages": [*messages, {"role": "user", "content":
                f"교사 라벨: {hint}\n이 라벨을 그대로 쓰고, 실제 상태에 맞는 짧고 자연스러운 한국어 대사 한 문장을 speech에 추가해."}],
            "format": schema,
            "stream": False,
            "think": False,
            "options": {"temperature": 0.4, "num_predict": 160, "num_ctx": 4096},
        }, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json"},
    )
    with urlopen(request, timeout=120) as response:
        result = json.load(response)
    reply = json.loads(result["message"]["content"])
    speech = reply.get("speech")
    if set(reply) != {*decision, "speech"} or any(reply[key] != value for key, value in decision.items()):
        raise ValueError(f"Teacher label mismatch: {reply}")
    if not isinstance(speech, str) or not speech.strip() or len(speech) > 100:
        raise ValueError(f"Invalid speech: {speech!r}")
    if not any("가" <= char <= "힣" for char in speech):
        raise ValueError(f"Speech is missing Korean text: {speech!r}")
    return {**decision, "speech": speech.strip()}


def record(messages, decision):
    return {"prompt": messages, "completion": [
        {"role": "assistant", "content": json.dumps(decision, ensure_ascii=False, separators=(",", ":"))}
    ]}


def line(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n"


def authored_rows(examples):
    for *example, speech in examples:
        messages, decision = make_example(*example)
        yield record(messages, {**decision, "speech": speech})


def update_existing():
    """Preserve reviewed teacher completions and revise only their prompts."""
    for filename, additions in (("train.jsonl", CONTRAST), ("eval.jsonl", ())):
        path = ROOT / filename
        rows = [json.loads(entry) for entry in path.read_text(encoding="utf-8").splitlines()]
        for row in rows:
            prompt = row["prompt"][-1]
            if OUTPUT_RULES[0] not in prompt["content"]:
                prompt["content"] = prompt["content"].replace(
                    RULE_ANCHOR, "\n".join((RULE_ANCHOR, *OUTPUT_RULES)), 1
                )
        seen = {line(row["prompt"]) for row in rows}
        for row in authored_rows(additions):
            if line(row["prompt"]) not in seen:
                rows.append(row)
        path.write_text("".join(map(line, rows)), encoding="utf-8")
    (ROOT / "eval_final.jsonl").write_text("".join(map(line, authored_rows(FINAL_EVAL))), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--teacher", default=TEACHER)
    parser.add_argument("--update-existing", action="store_true")
    args = parser.parse_args()
    if args.update_existing:
        update_existing()
        return
    train_path = ROOT / "train.jsonl"
    if train_path.exists():
        parser.error("train.jsonl already exists. Move it to preserve reviewed data before regenerating.")
    eval_rows = []
    for *example, speech in EVAL:
        messages, decision = make_example(*example)
        eval_rows.append(record(messages, {**decision, "speech": speech}))
    (ROOT / "eval.jsonl").write_text("".join(map(line, eval_rows)), encoding="utf-8")

    partial = ROOT / "train.partial.jsonl"
    completed = {}
    if partial.exists():
        for row in map(json.loads, partial.read_text(encoding="utf-8").splitlines()):
            completed[line(row["prompt"])] = row
    rows = []
    with partial.open("a", encoding="utf-8") as checkpoint:
        for *scenario, utterances in TRAIN:
            for utterance in utterances:
                messages, decision = make_example(*scenario, utterance)
                row = completed.get(line(messages))
                if row is None:
                    for attempt in range(3):
                        try:
                            row = record(messages, query(args.teacher, messages, decision))
                            break
                        except (OSError, ValueError):
                            if attempt == 2:
                                raise
                    checkpoint.write(line(row))
                    checkpoint.flush()
                rows.append(row)
                print(f"Train {len(rows)}", flush=True)
    assert not ({line(row["prompt"]) for row in rows} & {line(row["prompt"]) for row in eval_rows})
    rows.extend(authored_rows(CONTRAST))
    train_path.write_text("".join(map(line, rows)), encoding="utf-8")
    (ROOT / "eval_final.jsonl").write_text("".join(map(line, authored_rows(FINAL_EVAL))), encoding="utf-8")
    partial.unlink()
    print(f"Train: {len(rows)} | Eval: {len(eval_rows)}")


if __name__ == "__main__":
    main()
