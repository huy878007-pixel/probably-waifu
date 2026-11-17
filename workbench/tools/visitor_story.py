"""Expression choices for the two exclusive visitors' original dialogue."""
STORY_MOODS = {
    "waffle_enjoyer": {
        "dialog_foodie_1": "开心_原图",
        "dialog_foodie_2": "欢喜",
        "dialog_foodie_3": "平静",
        "dialog_foodie_4": "平静",
        "dialog_foodie_item_placed": "开心_原图",
        "dialog_foodie_accept": "欢喜",
        "dialog_foodie_done": "开心_原图",
        "dialog_foodie_arrest_client": "不开心",
        "dialog_foodie_returning_1": "开心_原图",
        "dialog_foodie_returning_2": "欢喜",
        "dialog_foodie_returning_3": "平静",
        "dialog_foodie_returning_4": "开心_原图",
    },
    "case_enjoyer": {
        "dialog_case_enjoyer_1": "开心_原图",
        "dialog_case_enjoyer_2": "平静",
        "dialog_case_enjoyer_3": "平静",
        "dialog_case_enjoyer_returning_1": "开心_原图",
        "dialog_case_enjoyer_open_1": "欢喜",
        "dialog_case_enjoyer_open_2": "欢喜",
        "dialog_case_enjoyer_open_3": "平静",
        "dialog_case_enjoyer_open_4": "愤怒",
    },
}


def apply_story_moods(characters):
    for identity, lines in STORY_MOODS.items():
        if identity in characters:
            character = characters[identity]
            character["story"] = {
                key: character["variants"]["mood:" + mood] for key, mood in lines.items()
            }
