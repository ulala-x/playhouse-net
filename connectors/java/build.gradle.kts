plugins {
    java
    `java-library`
}

group = "com.playhouse"
version = "0.1.0"

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

repositories {
    mavenCentral()
}

// Configure integration test source set
sourceSets {
    create("integrationTest") {
        java {
            srcDir("src/integrationTest/java")
        }
        compileClasspath += sourceSets.main.get().output
        runtimeClasspath += sourceSets.main.get().output
    }
}

// Integration test configuration
val integrationTestImplementation by configurations.getting {
    extendsFrom(configurations.implementation.get())
}

val integrationTestRuntimeOnly by configurations.getting

dependencies {
    // Netty for async networking
    implementation("io.netty:netty-all:4.1.107.Final")

    // LZ4 compression (optional)
    implementation("org.lz4:lz4-java:1.8.0")

    // Logging (SLF4J API)
    implementation("org.slf4j:slf4j-api:2.0.9")

    // Protobuf for message serialization
    implementation("com.google.protobuf:protobuf-java:3.25.1")

    // Testing
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.0")
    testImplementation("org.assertj:assertj-core:3.24.2")
    testRuntimeOnly("ch.qos.logback:logback-classic:1.4.11")

    // Integration test dependencies
    integrationTestImplementation("org.junit.jupiter:junit-jupiter:5.10.0")
    integrationTestImplementation("org.assertj:assertj-core:3.24.2")
    integrationTestImplementation("com.squareup.okhttp3:okhttp:4.12.0")
    integrationTestImplementation("com.google.code.gson:gson:2.10.1")
    integrationTestRuntimeOnly("ch.qos.logback:logback-classic:1.4.11")
}

tasks.test {
    useJUnitPlatform()
}

// Register integration test task
val integrationTest = tasks.register<Test>("integrationTest") {
    description = "Runs integration tests"
    group = "verification"

    testClassesDirs = sourceSets["integrationTest"].output.classesDirs
    classpath = sourceSets["integrationTest"].runtimeClasspath

    useJUnitPlatform()

    // Use environment variables for test server configuration
    environment("TEST_SERVER_HOST", System.getenv("TEST_SERVER_HOST") ?: "localhost")
    environment("TEST_SERVER_HTTP_PORT", System.getenv("TEST_SERVER_HTTP_PORT") ?: "28080")
    environment("TEST_SERVER_TCP_PORT", System.getenv("TEST_SERVER_TCP_PORT") ?: "28001")

    shouldRunAfter(tasks.test)
}

tasks.withType<JavaCompile> {
    options.encoding = "UTF-8"
}
