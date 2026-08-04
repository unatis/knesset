# Шрифты для OG-картинок

`OgImageService` рисует превью **одним** мультискриптовым шрифтом: в одном файле должны быть
иврит, арабский, кириллица, латиница и цифры. Смешивать несколько шрифтов внутри строки нельзя —
при fallback'е на отдельный ивритский шрифт bidi-движок SixLabors переставляет цифры
(«2026» → «0226»), а сабсеты Noto Sans Hebrew с googlefonts дают ещё и битые метрики глифов.

| Файл | Что это |
|---|---|
| `Title-Multiscript.ttf` | Rubik Bold (wght 700) |
| `Body-Multiscript.ttf` | Rubik Regular (wght 400) |

Rubik распространяется под SIL Open Font License 1.1 (текст лицензии — в `OFL.txt`),
её условия позволяют держать шрифт в репозитории и распространять вместе с приложением.
Покрытие всех четырёх письменностей проверено по таблице cmap.

## Как эти файлы получены

Upstream отдаёт только вариативный `Rubik[wght].ttf`, а SixLabors.Fonts читает у вариативного
файла лишь дефолтный инстанс — это минимум оси, Rubik Light, для заголовков слишком тонко.
Поэтому статические начертания нарезаны из вариативного файла через fontTools:

```bash
pip install fonttools
```

```bash
curl -sL -o Rubik.ttf "https://raw.githubusercontent.com/google/fonts/main/ofl/rubik/Rubik%5Bwght%5D.ttf"
```

```bash
python -m fontTools.varLib.instancer Rubik.ttf wght=700 --update-name-table -o Title-Multiscript.ttf
```

```bash
python -m fontTools.varLib.instancer Rubik.ttf wght=400 --update-name-table -o Body-Multiscript.ttf
```

`--update-name-table` обязателен: без него оба файла остаются с именем семейства «Rubik Light»,
и SixLabors не может отличить начертания друг от друга.

Заменить Rubik на другой шрифт можно, просто положив сюда два TTF под теми же именами, —
код менять не нужно. Главное требование к замене: одна гарнитура покрывает все письменности.
