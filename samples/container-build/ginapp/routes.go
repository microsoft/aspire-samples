package main

import (
	_ "embed"
	"net/http"

	"github.com/gin-gonic/gin"
)

// openAPIDocument describes the API (OpenAPI 3.1). It's served from /openapi.json and rendered by Scalar at /scalar.
//
//go:embed openapi.json
var openAPIDocument []byte

//go:embed scalar.html
var scalarPage []byte

// setupRoutes registers the API, its documentation, and (when enabled) the development-only admin endpoints.
func setupRoutes(router *gin.Engine, api *flagAPI, admin *adminAPI) {
	router.GET("/", func(c *gin.Context) { c.Redirect(http.StatusFound, "/scalar") })
	router.GET("/health", func(c *gin.Context) { c.JSON(http.StatusOK, gin.H{"status": "healthy"}) })
	router.GET("/openapi.json", func(c *gin.Context) { c.Data(http.StatusOK, "application/json", openAPIDocument) })
	router.GET("/scalar", func(c *gin.Context) { c.Data(http.StatusOK, "text/html; charset=utf-8", scalarPage) })

	flags := router.Group("/flags")
	flags.GET("", api.listFlags)
	flags.POST("", api.createFlag)
	flags.GET("/:key", api.getFlag)
	flags.PATCH("/:key", api.updateFlag)
	flags.DELETE("/:key", api.deleteFlag)
	flags.POST("/:key/evaluate", api.evaluateFlag)
	flags.POST("/:key/rollout/advance", api.advanceRollout)
	router.POST("/evaluate", api.evaluateFlags)

	if admin != nil {
		router.POST("/admin/simulate", admin.simulate)
		router.POST("/admin/reset", admin.reset)
	}

	router.NoRoute(func(c *gin.Context) {
		writeProblem(c, http.StatusNotFound, "No endpoint matches "+c.Request.URL.Path+". See /scalar for the API reference.", nil)
	})
	router.NoMethod(func(c *gin.Context) {
		writeProblem(c, http.StatusMethodNotAllowed, c.Request.Method+" isn't supported on "+c.Request.URL.Path+".", nil)
	})
}
