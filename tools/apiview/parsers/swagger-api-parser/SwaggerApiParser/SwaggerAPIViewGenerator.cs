using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SwaggerApiParser;

public class SwaggerApiViewGenerator
{
    public static async Task<SwaggerApiViewSpec> GenerateSwaggerApiView(SwaggerSpec swaggerSpec, string swaggerFilePath, SchemaCache schemaCache, string packageName = "", string swaggerLink = "")
    {
        SwaggerApiViewSpec ret = new SwaggerApiViewSpec
        {
            SwaggerApiViewGeneral =
            {
                info = swaggerSpec.info,
                swagger = swaggerSpec.swagger,
                host = swaggerSpec.host,
                schemes = swaggerSpec.schemes,
                consumes = swaggerSpec.consumes,
                produces = swaggerSpec.produces,
                security = swaggerSpec.security,
                securityDefinitions = swaggerSpec.securityDefinitions,
                xMsParameterizedHost = swaggerSpec.xMsParameterizedHost,
                swaggerLink = swaggerLink
            },
            fileName = Path.GetFileName(swaggerFilePath),
            packageName = packageName
        };

        AddDefinitionsToCache(swaggerSpec, swaggerFilePath, schemaCache);
        ret.SwaggerApiViewGeneral.xMsParameterizedHost?.ResolveParameters(schemaCache, swaggerFilePath);


        // If swagger doesn't have any path, it's common definition swagger. 
        if (swaggerSpec.paths.Count == 0)
        {
            return null;
        }


        foreach (var (currentPath, operations) in swaggerSpec.paths)
        {
            if (operations == null)
            {
                continue;
            }

            foreach (var (key, value) in operations.operations)
            {
                SwaggerApiViewOperation op = new SwaggerApiViewOperation
                {
                    operation = value,
                    method = key,
                    path = currentPath,
                    operationId = value.operationId,
                    description = value.description,
                    summary = value.summary,
                    tags = value.tags,
                    procudes = value.produces,
                    consumes = value.consumes,
                    xMSPageable = value.xMsPageable,
                    operationIdPrefix = Utils.GetOperationIdPrefix(value.operationId),
                    operationIdAction = Utils.GetOperationIdAction(value.operationId),
                    PathParameters = new SwaggerApiViewOperationParameters("PathParameters"),
                    QueryParameters = new SwaggerApiViewOperationParameters("QueryParameters"),
                    BodyParameters = new SwaggerApiViewOperationParameters("BodyParameters"),
                    HeaderParameters = new SwaggerApiViewOperationParameters("HeaderParameters"),
                    Responses = new List<SwaggerApiViewResponse>(),
                    xMsLongRunningOperation = value.xMsLongRunningOperaion
                };

                if (value.parameters != null)
                {
                    foreach (var parameter in value.parameters)
                    {
                        var param = parameter;
                        if (parameter.IsRefObject())
                        {
                            param = (Parameter)swaggerSpec.ResolveRefObj(parameter.Ref) ?? schemaCache.GetParameterFromCache(parameter.Ref, swaggerFilePath);
                        }

                        var currentSwaggerFilePath = swaggerFilePath;

                        if (param == null)
                        {
                            if (!Path.IsPathFullyQualified(parameter.Ref))
                            {
                                var referenceSwaggerFilePath = Utils.GetReferencedSwaggerFile(parameter.Ref, currentSwaggerFilePath);
                                var referenceSwaggerSpec = await SwaggerDeserializer.Deserialize(referenceSwaggerFilePath);
                                referenceSwaggerSpec.swaggerFilePath = Path.GetFullPath(referenceSwaggerFilePath);
                                AddDefinitionsToCache(referenceSwaggerSpec, referenceSwaggerFilePath, schemaCache);
                                param = schemaCache.GetParameterFromCache(parameter.Ref, referenceSwaggerFilePath);
                            }
                            else
                            {
                                continue;
                            }
                        }

                        var swaggerApiViewOperationParameter = new SwaggerApiViewParameter
                        {
                            description = param.description,
                            name = param.name,
                            required = param.required,
                            format = param.format,
                            In = param.In,
                            schema = schemaCache.GetResolvedSchema(param.schema, currentSwaggerFilePath),
                            Ref = param.Ref,
                            type = param.type
                        };


                        switch (param.In)
                        {
                            case "path":
                                op.PathParameters.Add(swaggerApiViewOperationParameter);
                                break;
                            case "query":
                                op.QueryParameters.Add(swaggerApiViewOperationParameter);
                                break;
                            case "body":
                                op.BodyParameters.Add(swaggerApiViewOperationParameter);
                                break;
                            case "header":
                                op.HeaderParameters.Add(swaggerApiViewOperationParameter);
                                break;
                        }
                    }
                }


                foreach (var (statusCode, response) in value.responses)
                {
                    var schema = response.schema;
                    var currentSwaggerFilePath = swaggerFilePath;

                    //Resolve ref obj for response schema.

                    if (response.schema != null)
                    {
                        // The initial refChain is the root level schema.
                        // There are some scenarios that the property of the root level schema is a ref to the root level itself (circular reference).
                        // Like "errorDetail" schema in common types.
                        LinkedList<string> refChain = new LinkedList<string>();
                        schema = schemaCache.GetResolvedSchema(schema, currentSwaggerFilePath, refChain);
                    }

                    var headers = response.headers ?? new Dictionary<string, Header>();

                    op.Responses.Add(new SwaggerApiViewResponse() {description = response.description, statusCode = statusCode, schema = schema, headers = headers});
                }

                ret.Paths.AddSwaggerApiViewOperation(op);
            }
        }

        if (swaggerSpec.definitions != null)
        {
            foreach (var definition in swaggerSpec.definitions)
            {
                ret.SwaggerApiViewDefinitions.Add(definition.Key, definition.Value);
            }
        }

        if (swaggerSpec.parameters != null)
        {
            foreach (var kv in swaggerSpec.parameters)
            {
                var param = kv.Value;
                var swaggerApiViewParameter = new SwaggerApiViewParameter
                {
                    description = param.description,
                    name = param.name,
                    required = param.required,
                    format = param.format,
                    In = param.In,
                    schema = schemaCache.GetResolvedSchema(param.schema, swaggerFilePath),
                    Ref = param.Ref,
                    type = param.type
                };
                ret.SwaggerApiViewGlobalParameters.Add(kv.Key, swaggerApiViewParameter);
            }
        }

        ret.Paths.SortByMethod();
        return ret;
    }

    public static void AddDefinitionsToCache(SwaggerSpec swaggerSpec, string swaggerFilePath, SchemaCache schemaCache)
    {
        var fullPath = Path.GetFullPath(swaggerFilePath);
        if (swaggerSpec.definitions != null)
        {
            foreach (var definition in swaggerSpec.definitions)
            {
                if (!schemaCache.Cache.ContainsKey(definition.Key))
                {
                    schemaCache.AddSchema(fullPath, definition.Key, definition.Value);
                }
            }
        }


        if (swaggerSpec.parameters != null)
        {
            foreach (var parameter in swaggerSpec.parameters)
            {
                if (!schemaCache.ParametersCache.ContainsKey(parameter.Key))
                {
                    schemaCache.AddParameter(fullPath, parameter.Key, parameter.Value);
                }
            }
        }
    }
}
