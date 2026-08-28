using CSharpFunctionalExtensions;
using Notion.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace Infrastructure.DataAccess
{
    internal static class NotionHelper
    {

        internal static DatabasesQueryParameters GetParameters(List<Filter> filters)
        {
            var queryParameters = new DatabasesQueryParameters
            {
                PageSize = int.MaxValue,
                Filter = new CompoundFilter
                {
                    And = filters,
                }
            };
            return queryParameters;
        }

        internal static Result<IReadOnlyList<string>> GetStringList(PropertyValue? p)
        {
            if (p is null)
                return Result.Failure<IReadOnlyList<string>>("Property is null.");

            switch (p)
            {
                case MultiSelectPropertyValue multiSelectPropertyValue:
                    return multiSelectPropertyValue.MultiSelect.Select(v => v.Name).ToList();
                default:
                    return Result.Failure<IReadOnlyList<string>>("Property value is not mapped to string list.");
            }
        }

        internal static Result<string> GetString(PropertyValue? p)
        {
            if (p is null)
                return Result.Failure<string>("Property is null.");

            switch (p)
            {
                case RelationPropertyValue relationPropertyValue:
                    var relationId = relationPropertyValue.Relation.FirstOrDefault()?.Id;
                    if (relationId is null)
                        return Result.Failure<string>("Property relation is null.");
                    return relationId;
                case RichTextPropertyValue richTextPropertyValue:
                    var text = richTextPropertyValue.RichText.FirstOrDefault()?.PlainText;
                    if (text is null)
                        return Result.Failure<string>("Property value is null.");
                    return text;
                case TitlePropertyValue titlePropertyValue:
                    var title = titlePropertyValue.Title.FirstOrDefault()?.PlainText;
                    if (title is null)
                        return Result.Failure<string>("Property value is null.");
                    return title;
                case SelectPropertyValue selectPropertyValue:
                    var select = selectPropertyValue.Select.Name;
                    if (select is null)
                        return Result.Failure<string>("Property value is null.");
                    return select;
                default:
                    return Result.Failure<string>("Property value is not mapped to string.");
            }
        }

        internal static Result<bool> GetBoolean(PropertyValue? p)
        {
            if (p is null)
                return Result.Failure<bool>("Property is null.");

            switch (p)
            {
                case CheckboxPropertyValue value:
                    return value.Checkbox;
                default:
                    return Result.Failure<bool>("Property value is not mapped to bool.");
            }
        }

        internal static Result<int> GetInt(PropertyValue? p)
        {
            return GetDouble(p).Map(d => (int)d);
        }

        internal static Result<double> GetDouble(PropertyValue? p)
        {
            if (p is null)
                return Result.Failure<double>("Property is null.");

            switch (p)
            {
                case FormulaPropertyValue formula:
                    if (formula.Formula.Number is null)
                        return Result.Failure<double>("Property value is null.");
                    return formula.Formula.Number.Value;
                case NumberPropertyValue number:
                    if (number.Number is null)
                        return Result.Failure<double>("Property value is null.");
                    return number.Number.Value;
                default:
                    return Result.Failure<double>("Property value is not mapped to int.");
            }
        }

        internal static Result<DateTimeOffset> GetDate(PropertyValue? p)
        {
            if (p is null)
                return Result.Failure<DateTimeOffset>("Property is null.");

            switch (p)
            {
                case CreatedTimePropertyValue createTime:
                    var createdTimeString = createTime.CreatedTime;
                    if (createdTimeString is null)
                        return Result.Failure<DateTimeOffset>("Property value is null.");

                    return DateTimeOffset.Parse(createdTimeString);

                case DatePropertyValue date:
                    var parsedDate = date.Date.Start;
                    if (parsedDate is null)
                        return Result.Failure<DateTimeOffset>("Property value is null.");

                    return parsedDate.Value;
                default:
                    return Result.Failure<DateTimeOffset>("Property value is not mapped to date.");
            }
        }

    }
}
