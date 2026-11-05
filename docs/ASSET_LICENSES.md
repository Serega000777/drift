# Реестр ассетов и данных

## Сторонние ассеты
**Нет.** Все модели, текстуры, материалы, частицы и звуки генерируются кодом проекта (`ProceduralMeshes`, `MaterialLibrary`, `CarRig`, `AcademyBuilder`, `VehicleAudio`). Шрифт — встроенный шрифт темы UI Toolkit (поставляется с Unity).

При добавлении любого ассета записать сюда: название, источник (URL), автор, лицензия (точный текст/ссылка), дата, требования атрибуции, права на коммерческое использование и распространение, изменения. Разрешённые источники для начала: Kenney (CC0), Quaternius (CC0), Poly Haven (CC0), проверенные позиции Unity Asset Store (EULA). Запрещено: рипы из других игр, логотипы и марки без разрешения, музыка/звуки без лицензии, модели реальных машин без прав.

| Ассет | Источник | Лицензия | Использование | Изменения |
|---|---|---|---|---|
| — | — | — | — | — |

## Справочные данные о реальных машинах и моторах
Используются только **числовые характеристики** (факты) для калибровки физики. Названия марок/моделей/кодов моторов хранятся в `referenceNote` / `EngineData.referenceCode` и **не показываются в релизном UI** без лицензии (только при `DC_SHOW_REFERENCE_NAMES`). Флаг уверенности у каждой записи.

Источники, использованные при сверке (2026-11-05):
- ВАЗ-2101 (1198 см³, 64 л.с., 4МТ, задний привод): [auto.ru](https://auto.ru/catalog/cars/vaz/2101/6256978/6256981/specifications/), [autospot.ru](https://autospot.ru/brands/vaz/2645/technical-parameters/engine-capacity/), [cyclowiki](https://cyclowiki.org/wiki/%D0%92%D0%B0%D0%B7-21011-21013), [ruwiki](https://ru.ruwiki.ru/wiki/%D0%92%D0%90%D0%97-2101), [drom.ru](https://www.drom.ru/catalog/lada/2101/).
- ВАЗ-2103 (1451 см³), ВАЗ-2106 (1568 см³): [За рулём, 1976](https://www.zr.ru/archive/zr/1976/10/sovietskaia-tiekhnika), [auto.ru 2106](https://auto.ru/catalog/cars/vaz/2106/6257105/6257183/specifications/), [auto.ru 2103](https://auto.ru/catalog/cars/vaz/2103/6257051/6257055/specifications/6257055__23109296/). Моменты, передаточные числа и главные пары — по памяти о заводских данных, **приблизительно**, требуют сверки с заводским руководством.
- 1JZ-GTE 280 PS @ 6200, 363 Н·м @ 4800: [Toyota JZ engine](https://wiki.krisyotam.com/content/wikipedia_en_all_nopic_2026-03/Toyota_JZ_engine).
- SR20DET: [drifted.com](https://drifted.com/sr20det-specs).
- 2JZ-GTE: источники расходятся ([slashgear](https://www.slashgear.com/1584318/jdm-engines-with-lots-of-power/), [8020automotive](https://8020automotive.com/2jz-vs-rb26-engine-guide-a-battle-of-jdm-legends/)) — помечено `approximate`.
- Массы свапов (мотор + КПП): [drive2.ru](https://www.drive2.ru/b/531771378144117878/) — данные сообщества, `approximate`.
- RB25DET, RB26DETT, 13B-REW, 4A-GE, CA18DET — публикуемые JDM-каталожные значения, **не сверены** в этой сессии, `approximate`.

## Трассы, серии, бренды
Японские трассы и профессиональные серии — только как семейства «Скоро» с `RequiresLicense`, без названий площадок и логотипов в UI. «Ледовое озеро» — оригинальная локация «по мотивам Байкала», с оговоркой, что это игра, а не рекомендация выезжать на лёд.
