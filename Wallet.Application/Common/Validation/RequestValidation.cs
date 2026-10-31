namespace Wallet.Application.Common.Validation;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

public static class RequestValidation
{
    public static bool TryValidate(object instance, ICollection<ValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var isValid = true;
        var context = new ValidationContext(instance);

        if (!Validator.TryValidateObject(instance, context, results, validateAllProperties: true))
        {
            isValid = false;
        }

        var type = instance.GetType();
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var primaryConstructor = constructors
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        if (primaryConstructor != null)
        {
            foreach (var parameter in primaryConstructor.GetParameters())
            {
                var validationAttributes = parameter.GetCustomAttributes<ValidationAttribute>(inherit: true).ToList();
                if (validationAttributes.Count == 0)
                {
                    continue;
                }

                var property = type.GetProperty(parameter.Name ?? string.Empty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var value = property?.GetValue(instance);

                var paramContext = new ValidationContext(instance)
                {
                    MemberName = parameter.Name
                };

                foreach (var attribute in validationAttributes)
                {
                    var result = attribute.GetValidationResult(value, paramContext);
                    if (result != null && result != ValidationResult.Success)
                    {
                        results.Add(result);
                        isValid = false;
                    }
                }
            }
        }

        return isValid;
    }
}
