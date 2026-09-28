// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.EntityFrameworkCore.Migrations;

namespace DDT.Server.Migrations;

// The hand-written part of M7FlowBuilder, kept apart because the migration is generated again once at the end of M7,
// when every track's model changes are in: carry this file over and call CopyAssignmentRules from Up again, after the
// Rules table is created. SQLite development databases are created from the model and have nothing to copy.
public partial class M7FlowBuilder
{
    // Today's assignment rules become the top of the ordered list, in the order SequenceResolver tries them: MAC rules
    // first, then model rules with the exact model before a prefix, the longer model first, a named manufacturer before
    // any, and the id last, compared as .NET compares Guids, which is PostgreSQL's order for uuid too. MAC rules among
    // themselves go by id: where rules name two addresses of one machine, the higher rule now wins, where the rule for its
    // primary address did. Ids are kept, so Deployment.RuleId still names the rule that chose a run.
    //
    // When is written as DdtJsonContext reads a ConditionNode: camelCase members, the operator by name. A MAC rule tests
    // MacAddress Equals the address, which holds when any of the machine's addresses is that one, as the rule matched
    // before; a model rule tests Manufacturer Equals, where it names one, and Model Equals, or Matches with the trailing *
    // for a prefix. Name is AssignmentRuleKeys.Describe's text with a capital first letter,
    // "MAC address 00:15:5D:01:02:03", "Model Dell Inc. Latitude 5440" or "Model Latitude 7* of any maker". The rules
    // set no values and give no roles.
    private static void CopyAssignmentRules(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            WITH ordered AS (
                SELECT
                    r.*,
                    r."Kind" = 'Mac' AS is_mac,
                    COALESCE(right(r."Model", 1) = '*', false) AS is_prefix,
                    COALESCE(char_length(r."Model"), 0) AS model_length,
                    regexp_replace(COALESCE(r."Mac", ''), '(..)(?!$)', '\1:', 'g') AS mac_text
                FROM ddt."AssignmentRules" AS r
            )
            INSERT INTO ddt."Rules" (
                "Id", "Position", "Name", "Description", "Enabled", "When", "TaskSequenceId", "Values", "RoleIds",
                "Revision", "CreatedUtc", "UpdatedUtc", "UpdatedByUserId", "UpdatedByName")
            SELECT
                o."Id",
                (row_number() OVER (ORDER BY NOT o.is_mac, o.is_prefix, o.model_length DESC, o."Manufacturer" IS NULL, o."Id"))::integer - 1,
                CASE
                    WHEN o.is_mac THEN 'MAC address ' || o.mac_text
                    WHEN o."Manufacturer" IS NOT NULL THEN 'Model ' || o."Manufacturer" || ' ' || o."Model"
                    ELSE 'Model ' || o."Model" || ' of any maker'
                END,
                o."Description",
                true,
                (CASE
                    WHEN o.is_mac THEN jsonb_build_object(
                        'kind', 'test', 'variable', 'MacAddress', 'operator', 'Equals', 'value', o.mac_text)
                    ELSE jsonb_build_object(
                        'kind', 'all',
                        'parts',
                        CASE
                            WHEN o."Manufacturer" IS NULL THEN '[]'::jsonb
                            ELSE jsonb_build_array(jsonb_build_object(
                                'kind', 'test', 'variable', 'Manufacturer', 'operator', 'Equals', 'value', o."Manufacturer"))
                        END
                        || jsonb_build_array(jsonb_build_object(
                            'kind', 'test',
                            'variable', 'Model',
                            'operator', CASE WHEN o.is_prefix THEN 'Matches' ELSE 'Equals' END,
                            'value', o."Model")))
                END)::text,
                o."TaskSequenceId",
                '[]',
                '[]',
                1,
                o."CreatedUtc",
                o."UpdatedUtc",
                o."UpdatedByUserId",
                o."UpdatedByName"
            FROM ordered AS o;
            """);
}
