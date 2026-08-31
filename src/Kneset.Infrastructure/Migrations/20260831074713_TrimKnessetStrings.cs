using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kneset.Infrastructure.Migrations
{
    /// <summary>
    /// Разовая чистка обрамляющих пробелов в строках, пришедших из выгрузки Кнессета.
    ///
    /// Часть значений приходила с висячим пробелом — «הליכוד » вместо «הליכוד».
    /// На экране это не видно, но фильтр законопроектов по фракции сравнивает
    /// названия через ==, и расхождение в один пробел молча делает отбор пустым.
    /// Синхронизация теперь чистит строки на входе (KnessetSyncService.Clean),
    /// эта миграция приводит в порядок то, что уже лежит в базе.
    ///
    /// Схему не меняет — только данные, поэтому идемпотентна и безопасна для
    /// повторного применения. Пустые строки после обрезки становятся NULL:
    /// значение из одних пробелов означало отсутствие значения, а не пустоту.
    /// </summary>
    public partial class TrimKnessetStrings : Migration
    {
        /// <summary>Колонки, в которые попадают строки из внешнего источника.</summary>
        private static readonly (string Table, string[] Columns)[] Targets =
        [
            ("Persons", ["FirstName", "LastName", "GenderDesc", "Email", "FactionName", "PhotoUrl"]),
            ("Bills", ["Name", "NameRu", "StatusDesc", "SubTypeDesc", "SummaryLaw"]),
            ("BillSessions", ["StatusDesc"]),
            ("IsraelLaws", ["Name", "ValidityDesc"]),
            ("LawActs", ["Name"]),
            ("LawAmendments", ["ActName", "BindingTypeDesc", "AmendmentTypeDesc"]),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, columns) in Targets)
            {
                foreach (var column in columns)
                {
                    // NULLIF отдельным шагом: btrim от строки из одних пробелов даёт
                    // пустую строку, а она в фильтрах ведёт себя иначе, чем NULL.
                    migrationBuilder.Sql($"""
                        UPDATE "{table}"
                        SET "{column}" = NULLIF(btrim("{column}"), '')
                        WHERE "{column}" IS NOT NULL
                          AND "{column}" <> NULLIF(btrim("{column}"), '');
                        """);
                }
            }

            // FirstName, LastName, Name и ActName в модели не допускают NULL —
            // предыдущий шаг мог обнулить их, если значение состояло из пробелов.
            foreach (var (table, column) in new[]
                     {
                         ("Persons", "FirstName"), ("Persons", "LastName"),
                         ("Bills", "Name"), ("IsraelLaws", "Name"),
                         ("LawActs", "Name"), ("LawAmendments", "ActName"),
                     })
            {
                migrationBuilder.Sql($"""
                    UPDATE "{table}" SET "{column}" = '' WHERE "{column}" IS NULL;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратного хода нет: какие именно пробелы стояли в каждой строке,
            // после обрезки не восстановить. Данные при этом не потеряны —
            // следующий прогон синхронизации перезапишет поля из источника.
        }
    }
}
